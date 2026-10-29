# Proposal

## Why

The first security audit of this repo (`security-audit-skill/tacticus-planner-api/run-1`, source ref `5c9d6be`) confirmed two problems, both in guild authority:

- **Medium: a user can claim another player's identity.** The Tacticus user id is self-asserted. A user can claim the id of a guild Leader or Co-Leader who has no Planner profile yet. After the next sync they inherit that role and can sync or delete the guild. If the real owner then saves their own id, they get a 500.
- **Low: purge trusts a stale role.** `DELETE guild` authorized on the persisted role. A Co-Leader who had been demoted or removed upstream, or who had cleared their id, could still delete the guild's Planner record and its raid history, which cannot be restored.

The audit also recorded hardening notes:

- unbounded per-profile cardinality, with a mechanism reproduced at 2.64 MB per goal;
- crypto hygiene: no associated data, the AES key reused for HMAC, no `ValidateOnStart`;
- CI supply chain: unpinned actions and images, a free-form deploy ref, broad `id-token`;
- availability: anonymous readiness doing a database round-trip, retries of unsafe methods, no V1 response cap, no per-guild sync cooldown;
- correctness: a duplicate id returned 500, purged names stayed visible to guildmates, and `DELETE account` returned 204 even when the Entra delete did nothing.

The owner made these decisions:

- **Minimal guild fix.** The upstream Tacticus API cannot prove which user owns a key, so a self-asserted Tacticus user id remains a known, documented residual risk. This change closes the stale-role purge path and the 500. It also severs stale links when the id changes. It does not prove ownership.
- **Stale guild reads are accepted.** A departed or demoted member keeps read access until the next sync. Purge must re-check the fresh upstream role.
- **Caps instead of pagination:**
  - 200 non-archived goals per goal type.
  - 100 non-archived projects. Archiving frees a slot, because projects cannot be deleted.
  - 50 teams per Legendary Event lane.
- **Crypto migration may delete data.** Re-hashing with the new subkey cannot recover the raid observations, so the migration deletes them. The V2 destructive-changes policy allows this: V2 is greenfield and pre-production.

## What Changes

- **Guild authority**
  - `DELETE guild` requires a configured Tacticus user id and a persisted Leader/Co-Leader role. It then re-verifies the role against a fresh upstream roster, using the guild's stored token, before deleting.
  - If the upstream check fails, purge deletes nothing (400/503). It returns 403 when the caller is not Leader/Co-Leader upstream, or when the token now answers for a different guild.
  - `DELETE guild` joins the `upstream-slow` rate-limit bucket.
- **Guild sync**
  - `POST guild/sync` rejects persisted non-leaders before any upstream call.
  - It claims a per-guild one-minute cooldown atomically; inside the cooldown it returns 429 `guildSyncCooldown` with `Retry-After`.
- **Tacticus user id**
  - `PUT account/tacticus-integration` and the V1 import validate the id as a GUID and store it in canonical form.
  - A duplicate returns 409 `tacticusUserIdInUse`; in the V1 import it is the `tacticus_user_id_conflict` part code.
  - A non-GUID returns 400; in the V1 import it is the `invalid_tacticus_user_id` part code.
  - Changing or clearing the id unlinks the profile's guild-member row in the same save.
- **Account purge**
  - `DELETE account` clears the guild-visible name and last-active copies in the same transaction as the profile delete.
  - It reports `X-Sign-In-Account-Deleted: true|false`, which CORS exposes.
- **Caps and bounds**
  - At most 200 non-archived goals per type on `POST goals`, on leaving Archived (`POST goals/status`), and in the V1 goal import. The error is 400 `goalLimitReached`.
  - At most 100 non-archived projects on `POST projects`, which returns 400 `projectLimitReached`, and in the V1 goal import.
  - At most 50 teams per lane on `POST …/teams`, which returns 400 `legendaryEventTeamLimitReached`, and in the V1 LE import.
  - Per-goal lists are bounded:
    - snapshot resource lists: at most 250 distinct ids of at most 128 characters, each a catalog upgrade material;
    - farming locations: at most 100, each validated against the catalog;
    - upgrade targets: at most 250, accepted only on Upgrade goals;
    - `DependsOnIndex` entries must be distinct.
  - V1 imports read at most 500 goals and 100 events, list at most 1000 goal outcomes, and list at most 200 issues per event.
- **Column encryption**
  - New writes use the version-2 envelope `tpenc:2:…`, with the `table.column` name as AES-GCM associated data.
  - The legacy envelope still decrypts.
  - Keyed hashes use an HKDF-SHA256 subkey.
  - Options are validated on start.
  - After migrations, a startup backfill behind an advisory lock re-encrypts and re-hashes legacy rows.
  - Migration `DeleteRaidObservationsForHashSubkey` deletes the stored Guild Raid observations.
- **Outbound HTTP and health**
  - The standard resilience handler no longer retries unsafe methods.
  - The V1 client no longer follows redirects and caps its response buffer at 16 MB.
  - An oversized or failed V1 profile read becomes the existing 400 "The V1 profile could not be retrieved."
  - `/health/ready` is output-cached for 5 s under one key.
- **CI and image**
  - Every third-party action is pinned to a commit SHA, and both base images are pinned by digest.
  - `cd-stage` deploys only `main`'s own commit, and the free-form `ref` input is removed.
  - `id-token: write` is granted per job.
  - The `containerapp` CLI extension is pinned.
  - `appsettings.Development.json` is excluded from the published image.
  - Local secret files are git-ignored.

## Capabilities

### New Capabilities

- `guild-authority`: who may sync and purge a registered guild, the per-guild sync cooldown, and the accepted stale-read policy.
- `tacticus-user-id`: GUID validation, canonical storage, uniqueness (409), and unlinking the guild member when the id changes.
- `account-purge`: the guild-visible copies cleared with the profile, and the sign-in-deletion header.
- `project-count-limit`: at most 100 non-archived projects per profile.
- `column-encryption`: the column-bound envelope, legacy read with startup backfill, the HKDF hash subkey, and startup validation.
- `outbound-http-safety`: no retries of unsafe methods, and the V1 client's redirect and size bounds.
- `health-endpoints`: readiness output caching.

### Modified Capabilities

- `goal-creation`: the per-type goal cap and the per-goal list bounds.
- `goal-bulk-status`: leaving Archived respects the per-type cap.
- `goal-target-editing`: the upgrade-target count bound on edit.
- `goal-target-model`: farming-location ids validated against the catalog per goal type and capped.
- `legendary-event-plans`: at most 50 teams per lane.
- `v1-goal-import`: the source-goal and outcome-list bounds, and the goal and project caps.
- `v1-legendary-event-import`: the event bound, the per-lane team cap, and issue-list truncation.
- `upstream-call-rate-limiting`: `DELETE guild` is limited as `upstream-slow`.
- `api-error-contract`: the 409 `tacticusUserIdInUse` and 429 `guildSyncCooldown` issue codes.

## Impact

- **Code**
  - `src/TacticusPlanner.Api`: Guilds, TacticusIntegration, AccountManagement, Goals, Projects, LegendaryEventPlans, V1Import, `Program.cs`, the `.csproj`, and the `Dockerfile`.
  - `src/TacticusPlanner.Domain`: validation constants.
  - `src/TacticusPlanner.Persistence/Encryption` and `PlannerDbContext`.
  - `orchestration/TacticusPlanner.ServiceDefaults/Extensions.cs`.
  - `.github/workflows` and `.gitignore`.
- **Migration**
  - `20261029203812_DeleteRaidObservationsForHashSubkey` deletes all `guild_raid_sync_states` and `guild_raid_seasons` rows; attacks cascade from seasons. This is destructive by design, and the next raid refresh observes them again.
  - The startup backfill rewrites every encrypted column and keyed hash on the profiles, `tacticus_integrations`, guilds and `guild_members` tables once.
- **Contract**
  - `artifacts/openapi/TacticusPlanner.Api.json` is regenerated for the new statuses and descriptions.
  - No request or response schema gains a field.
- **Companion apps change**
  - There is no paired `tacticus-planner-apps` change yet. Until one exists, the frontend may surface the new 4xx responses as generic errors. Follow-up for the frontend:
    - 400 `goalLimitReached` on `POST goals` and `POST goals/status`.
    - 400 `projectLimitReached` on `POST projects`.
    - 400 `legendaryEventTeamLimitReached` on `POST …/teams`.
    - 429 `guildSyncCooldown` with `Retry-After` on `POST guild/sync`, alongside the existing `rateLimited`.
    - `DELETE guild` now returns 400, 409, 429 and 503. Its 403 now also covers "no Tacticus user id", "no stored token" and "not Leader/Co-Leader upstream".
    - `POST guild/sync` returns 403 for a persisted non-leader before any upstream call.
    - `PUT account/tacticus-integration` returns 400 for a non-GUID user id and 409 `tacticusUserIdInUse`.
    - The `X-Sign-In-Account-Deleted` header on `DELETE account` 204 tells the client whether the Entra account is actually gone.
    - New V1 import codes:
      - part codes `invalid_tacticus_user_id` and `tacticus_user_id_conflict`;
      - goal outcome codes `goal_limit_reached`, `project_limit_reached`, `too_many_goals` and `outcomes_truncated`;
      - LE issue codes `lane_team_limit_reached` and `issues_truncated`.
- **Infra follow-up**
  - `V1Api:FunctionsKey` should move to a Key Vault secret so it can be rotated. This change leaves it unchanged; the key is already public in the V1 SPA bundle.
- **Residual risk**
  - Someone who knows an unlinked Leader's or Co-Leader's GUID can still claim that self-asserted id. After the next sync they can also sync and purge, because the fresh upstream roster really does give that id the role.
  - The fix narrows this risk but does not close it:
    - The real owner now gets 409 `tacticusUserIdInUse` instead of a 500, which shows the id was claimed.
    - Former and demoted role holders lose purge.
    - A changed id loses its guild link at once.
  - Closing the risk needs upstream proof of key ownership, which the Tacticus API does not offer (see design.md).
