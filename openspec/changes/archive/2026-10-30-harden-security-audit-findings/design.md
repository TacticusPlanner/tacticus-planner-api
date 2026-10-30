# Design

## Context

See proposal.md and the audit report (`security-audit-skill/tacticus-planner-api/run-1/REPORT.md`, `FINDINGS-DETAIL.md`).

This is an API-only change:

- No paired `tacticus-planner-apps` change exists yet. The proposal lists, as a follow-up, the new issue codes, statuses and headers the frontend needs.
- No catalog datasets, served projections or manifest change.
- No request or response schema gains a field. The shared contract surface is the endpoints' status codes, `issueCode` values and the `X-Sign-In-Account-Deleted` header, documented in the regenerated `artifacts/openapi`.

## Decisions

### 1. Guild: the minimal fix the owner chose

The audit's preferred fix was to bind `TacticusUserId` to proven ownership of a key. The upstream `/player` response carries no user id, so a key cannot prove whose id it is. The owner chose the minimal fix:

- **Purge re-verifies against a fresh upstream roster.**
  - `DELETE guild` requires a configured Tacticus user id, a persisted Leader/Co-Leader role, and a stored guild token.
  - It then runs `GuildSyncService.SynchronizeAsync` with that stored token and authorizes only on `Success`.
  - Every other result maps exactly as for sync: 400 rejected/malformed, 403 not Leader/Co-Leader, 409 conflict, 503 unavailable. Any of them deletes nothing; the purge fails closed.
  - If the token now answers for a different guild than the caller's link, the result is 403.
  - The persisted-role check runs first, so plain members never spend the token.
  - Because purge now calls upstream, it gets the `upstream-slow` bucket.
- **Sync pre-gates on the persisted role and gets a per-guild cooldown.**
  - `POST guild/sync` returns 403 for a persisted non-leader before any upstream call. The fresh roster stays the authority.
  - A one-minute cooldown on `guilds.last_sync_attempted_at` is claimed with a conditional `UPDATE … WHERE last_sync_attempted_at IS NULL OR <= now-1min`, before the upstream call. Concurrent callers therefore cannot both pass, and failed attempts count.
  - While the cooldown is open the response is 429 `guildSyncCooldown`, with `Retry-After = max(1, ceil(remaining seconds))`. This mirrors the existing Guild Raid refresh cooldown.
  - It is separate from the per-profile `rateLimited` 429.
- **Tacticus user id hygiene.**
  - One helper, `TacticusUserIdAssignment`, is used by `PUT account/tacticus-integration` and the V1 import.
  - It parses the id as a GUID and stores the canonical `Guid.ToString()` form, the form the roster hash uses. Two spellings of one id can no longer both be claimed, and the hash matches the roster's.
  - The unique-index violation on `ix_profiles_tacticus_user_id_hash` becomes 409 `tacticusUserIdInUse` (it used to be a 500). In the import it becomes the part code `tacticus_user_id_conflict`.
  - When the hash changes, including on clear, the profile's guild-member link (`profile_id`, `linked_player_name`, `last_active_in_planner_on`) is severed in the same save. A link made under an old id can no longer authorize anything, and the next sync relinks it from the new id.
- **Residual risk, accepted and documented.**
  - Someone who knows the GUID of an unlinked Leader/Co-Leader can still claim it, and after the next sync gets that role. The fresh roster confirms the role, so the re-check above does not stop this case.
  - What changes:
    - The real owner gets a 409, which makes the claim visible, instead of a 500.
    - Departed and demoted role holders and cleared ids lose purge.
    - A changed id loses its link at once.
  - Closing the risk fully needs upstream proof of ownership. It is out of scope until the Tacticus API offers one.

### 2. Stale guild reads are accepted

The owner set this policy during the audit: a departed or demoted member keeps read access (`GET guild`, `GET guild/raid-status`, raid refresh) until the next sync. Only the destructive purge must see the fresh role. Membership lookups by profile use `SingleOrDefault`, because `guild_members.profile_id` is unique.

### 3. Account purge

- `DELETE account` clears the guild-visible copies (`linked_player_name`, `last_active_in_planner_on`) and nulls `profile_id`, bumping `revision`. This runs in the same transaction, under the execution strategy, as the profile delete, so guildmates stop seeing the name without waiting for a sync.
- The best-effort Entra delete now reports its result in `X-Sign-In-Account-Deleted` (`true` only when it really happened). The header is added to the CORS exposed headers.
- The status stays 204 and the request stays idempotent.

### 4. Caps instead of pagination

- **Goals: 200 per `GoalType`, non-archived only** (Active, Paused, Completed).
  - Counted from the database under the profile lock (`ProjectGoalPlanningService.FindGoalLimitExceededAsync`) at every place that grows the set:
    - `POST goals`, as a whole batch;
    - `POST goals/status` leaving Archived;
    - the V1 goal import, where each staged goal spends a slot and a goal without room is skipped with `goal_limit_reached`. Synthesized prerequisites are skipped the same way, and dependents proceed without the edge.
  - Archiving frees a slot.
  - `GET goals` stays unpaginated; the cap bounds the non-archived list instead. Archived goals are read only on `?archived=true`.
- **Projects: 100 non-archived per profile**, counted under the profile lock.
  - There is no project delete, so counting archived projects could never be freed; archiving frees a slot.
  - The V1 goal import's own project counts: at the cap, every creatable candidate fails with `project_limit_reached` and nothing is created.
- **LE teams: 50 per lane.**
  - Checked inside the plan writer's mutation, so it is consistent with the revision check. The writer returns `Rejected`, which maps to 400 `legendaryEventTeamLimitReached` and leaves the revision unchanged.
  - The V1 LE import drops teams past 50 per lane with one `lane_team_limit_reached` issue per lane. Its de-duplication uses an order-insensitive member-set key and a precomputed per-lane sort order instead of a rescan for each team.
- **Other bounds as implemented:**
  - Snapshot resource lists: at most 250 distinct trimmed ids of at most 128 characters, each a catalog upgrade material.
  - Farming locations: at most 100, catalog-validated per goal type on create and on the details edit. Unlock ids must be the character's regular shard nodes; Rank, Ability and Upgrade ids must be campaign battles; an Ascension goal takes none.
  - Upgrade targets: at most 250, and an upgrade target only on Upgrade goals.
  - `DependsOnIndex` entries must be distinct.
  - V1 import: at most 500 source goals, read in priority order, with one `too_many_goals` outcome for the rest. At most 1000 goal outcomes, the last being `outcomes_truncated`. At most 100 V1 events, with the rest dropped silently. At most 200 issues per event, the last being `issues_truncated`.

  These caps sit well above real V1 data (about 100 goals and a few dozen events). They exist to bound request and response size, not to shape normal use.

### 5. Column encryption

- **Envelope v2 with the column as associated data.**
  - New writes are `tpenc:2:{keyVersion}:{nonce}:{tag}:{ciphertext}`, with the `table.column` name as the AES-GCM AAD.
  - A ciphertext copied into another encrypted column fails to decrypt.
  - The binding is to the column, not the row: EF value converters never see the row they convert, so row binding is not possible here.
  - Only the versioned envelope is read; the unversioned pre-change envelope is rejected.
- **HKDF hash subkey.** HMAC-SHA256 lookup hashes use `HKDF-SHA256(currentKey, info "tacticus-planner/column-hash/v1")` instead of the AES key itself.
- **Startup validation.** `ColumnEncryptionOptions` is validated on start: the current version names a key, and every key decodes (base64 or base64url) to 32 bytes. OpenAPI document generation skips this check.
- **No backfill or data migration.** No environment holds data that must survive: staging is recreated and production is provisioned after this change, so there is no backfill and no legacy-envelope read. Existing local databases are reset.
- **Key rotation.** A new `CurrentKeyVersion` re-keys new writes; old versions still decrypt. Hash lookup across key versions, as the audit suggested, is not built, because rotation would also change every hash. Add a multi-version lookup when the first rotation is planned.

### 6. Outbound HTTP, health, CI and image

- **Resilience.** `AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods())`. A timed-out POST, PUT, PATCH or DELETE (V1 login included) is no longer replayed upstream.
- **V1 client.**
  - `AllowAutoRedirect = false`, because the client sends the default `x-functions-key` header and a redirect would replay it to the `Location` host.
  - `MaxResponseContentBufferSize = 16 MB`. A real `users/me` body is a few hundred KB.
  - An over-cap body, or any `HttpRequestException`, reads as "no profile", which becomes the existing 400 "The V1 profile could not be retrieved." It used to escape as a 500.
- **Readiness.**
  - `/health/ready` stays anonymous and is output-cached for 5 s under a single key.
  - The custom policy ignores query, Host and Authorization, so a caller cannot force a fresh database round-trip.
  - Healthy and unhealthy results are both cached.
  - `/health/live` stays uncached.
- **CI and image.**
  - Every third-party action is pinned to a commit SHA, with the version in a comment.
  - Both base images are pinned by digest.
  - `cd-stage`: the free-form `inputs.ref` is removed, and checkout uses `github.sha`. A job guard `github.ref == 'refs/heads/main'` makes a dispatch from any other branch skip. `id-token: write` is granted only on the two jobs that sign in to Azure.
  - The `containerapp` extension is pinned to `1.3.0b5`.
  - `appsettings.Development.json` is `CopyToPublishDirectory="Never"`.
  - `.env*`, `*.pem`, `*.key`, `*.pfx` and `appsettings.*.local.json` are git-ignored.

## Migration and backfill

- No EF Core migration and no backfill. No environment holds data that must survive: staging is recreated and production is provisioned after this change, so there is no backfill and no legacy-envelope read.
- **Rollback.** Code from before this change cannot read v2 envelopes, and its hashes would not match. A rollback therefore needs a restore. This is acceptable before production.

## Risks / Trade-offs

- **Residual identity claim** (see 1). The claim stays possible for anyone who knows a GUID. It is documented, and the 409 makes a squat visible.
- **Purge now depends on Tacticus availability.** During an outage purge returns 503. A leader whose token is revoked must re-register with a fresh token before purging. Both cases fail closed by design.
- **Lost raid history.** The deletion is one-off and pre-production. Current-season data returns on the next refresh.
- **Frontend shows generic errors for the new codes** until the apps follow-up lands.
- **Sync cooldown spans the whole guild.** Two leaders syncing within a minute: the second gets 429 with `Retry-After`.
- **Readiness can lag a real outage by up to 5 s.**
