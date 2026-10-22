# Design

## Context

See proposal.md — Why. Current schema: 24 tables in `public`, UUIDv7 keys, global profile query filters on 17 entities, `Revision` concurrency tokens on 8 aggregates, AES-GCM columns with HMAC lookup side-columns, jsonb via `OwnsOne/OwnsMany().ToJson()` (EF Core 10 `ComplexCollection().ToJson()` is known to fail at query time on this stack — see `GoalConfiguration.cs` comment — so owned types stay). The deferrable unique constraint on `(profile_id, global_priority)` exists only as raw migration SQL.

## Goals / Non-Goals

**Goals:**
- Goal mutation = one profile lock + ≤ 3 statements; no denormalized membership columns to resync.
- LE plan operation = 1 SELECT + 1 UPDATE.
- Planning pages need `GET me/goals` + `GET me/projects` (+ settings/overrides) and nothing per goal or per project.
- One error shape; one route convention (`me/…` for everything profile-owned).
- Schema fully described by the EF model plus one documented raw statement.

**Non-Goals:**
- Named roster snapshots, multi-profile accounts, GraphQL/aggregate "planning-state" endpoint, pagination of goals/projects, output caching, named Postgres schemas (ADR 0001 superseded).

## Decisions

### Schema

1. **Slots scoped by profile, enforced in the app only.**
   No slot index and no `rank_target_key` column: `FindConflictAsync` checks the profile's in-flight goals (Rank by `RankTargetKey.For(config)`) under the profile `FOR NO KEY UPDATE` lock, which every goal write already takes, so racing writers are serialized without a constraint. Dropped in review (#112): the derived column needed a refresh after every config write, and the rule (Rank milestones, possibly Ascension milestones later) should change without a migration. Cost: a future write path that skips the lock or the check can create duplicates; the lock's doc comment and the spec say so. `GoalConflictDetection` becomes a single query on `goals`; `SyncOccupancyAsync`, `OccupiesInFlightSlot` and the per-project `FOR UPDATE` chain in `ProjectGoalPlanningService` are deleted; the profile `FOR NO KEY UPDATE` lock stays (it serializes the global order). Alternative: keep per-project slots with the denormalization — rejected by the owner (slot is a per-unit planning rule, projects are grouping).
2. **`profiles` absorbs `accounts`.** Pre-processor query becomes `SELECT id FROM profiles WHERE issuer=@i AND subject=@s`. `AccountId` type removed; `AnalyticsIdentityDeriver` keys on `ProfileId`. Reversal path documented in the proposal.
3. **LE plan as one row:** `teams jsonb` (`[{id, laneId, name, sortOrder, members:[{unitId, reserve, position}], objectiveIndexes:[…], runDepths:[{run, expectedBattleClears, source, recordedAt}]}]`), `runs jsonb` (keyed by run), `annotations jsonb` (`[{laneId, battleIndex, objectiveId, status, updatedAt}]`), all `OwnsMany().ToJson()`. Mutations become in-memory edits of the loaded aggregate followed by one `SaveChanges` guarded by `Revision`; `LegendaryEventTeamMutations`' intermediate save disappears. Invariants (lane ∈ {alpha,beta,gamma}, 0 ≤ position < 5, run range, depth ≥ 1, unique unit per team, unique `(lane, sortOrder)`) move into `LegendaryEventValidation` and are asserted by the writer before save. The run-record `teams` jsonb already uses this shape — one canonical record type (`LegendaryEventTeamRecord`) serves both.
4. **Roster inline on run records** (`roster jsonb` = `{characters, mows}`); `RosterSnapshot` entity, table, `ix_…_roster_snapshot_id` and `RosterSnapshotId` removed. The recorder rewrites the column in place as today.
5. **Attack units inline** (`units jsonb` = `[{unitId, kind, position, power}]`). Dedup by `content_hash`, the `(season, user_hash, completed_at)` index and the retention rules are unchanged. If a future feature needs unit-level queries, add a GIN index on `units` then.
6. **Timestamps:** `created_at`/`updated_at` only on aggregate roots and on `guild_members` (sync bookkeeping). `project_goals` keeps `created_at` (membership time is shown). Attacks keep `completed_at`/`started_at` (domain facts) and drop the audit pair.
7. **Validator-over-CHECK:** enum columns stay `text`; `GoalValidation`/`ProjectValidation`/`LegendaryEventValidation` own the allowed sets (already partly true). Remaining DB constraints: PK/FK/unique/not-null, the deferrable priority uniqueness, and `ck_goals_global_priority_in_flight` (the one invariant the app cannot see atomically). Alternative: Postgres enums — rejected, every added value is a migration for no query benefit.
8. **Migration reset:** delete `Persistence/Migrations/*`, run `dotnet ef migrations add InitialCreate`, then append to its `Up` the raw `ALTER TABLE goals ADD CONSTRAINT ix_goals_profile_global_priority UNIQUE (profile_id, global_priority) DEFERRABLE INITIALLY DEFERRED;` with a comment pointing to `GoalConfiguration`. Guard: a Persistence integration test asserts the constraint exists and is deferrable, so a future squash cannot lose it.

### API

9. **One goal read shape.** `GoalSummaryResponse` is deleted; `GoalDetailResponse` (minus `events`) is the list item. `projectIds` comes from one `project_goals` query per list (`GROUP BY goal_id`), not per goal. Payload per goal grows by the `config`/`snapshot` jsonb (~1–3 KB); for 100 goals that is one ~200 KB compressed-to-~30 KB response instead of 100 requests. `ProjectSummaryResponse` gains `goalIds` from the same join. Both lists default to non-Archived goals and accept `?archived=true` for Archived goals only; the projects selector filters membership ids, not project rows. Bidirectional membership and counts are compared using the same selector.
10. **Mutation routes.** `PUT me/goals/{id}` = today's combined edit (sections optional, each validated as its endpoint did, one transaction, `expectedRevision`), returning `{ goal, order }`: `goal` is updated detail and `order` is `{ revision, goalIds }` when priority was supplied, otherwise null. Creation uses one request-level `startPaused` boolean, default false; explicit null/non-boolean or a per-spec flag is rejected. `POST me/goals/status` `{goalIds[], status, expectedOrderRevision?}` runs under the profile lock, applies the lifecycle rules per goal, returns `{goals[], orderRevision}` on success, with conflicts represented only by an atomic 409 problem; a conflict on any goal fails the whole request (atomic — matches the bulk-membership precedent). Alternative: keep per-goal status route for single clicks — rejected, one route with a one-element array.
11. **Errors.** One shared problem writer, FastEndpoints `ErrorResponseBuilder`, status-code handling, and authentication/authorization hooks emit `application/problem+json` with required fields and `issueCode`. Cover framework 404/405/401/403 as well as validation, 429, upstream failures, and unhandled exceptions; retain `Allow`/`WWW-Authenticate` headers. Known concurrency conflicts re-read the resource in a clean scope for `current`, including the settings read shape; stale-order `current` uses `{ revision, goalIds }`. Once headers start, never rewrite status/headers or append JSON: log the trace id and abort an unexpected failed response.
12. **Catalog URLs** in the manifest become `game-catalog/{dataset}/{envelopeHash}`; `envelopeHash` covers the full unencoded envelope, including release metadata and data, while the manifest dataset hash remains the payload digest. The handler validates the envelope hash against the captured snapshot (404 otherwise) and sets `immutable` caching with encoding-specific strong ETags and `Vary: Accept-Encoding`. Metadata-only releases change the immutable URL as well as its validators. The unhashed route stays as `must-revalidate` for ad-hoc use.
13. **Run-record paging:** `?after=<opaque-cursor>&limit=20` (default 20, max 100), ordered by `(started_at DESC, id DESC)` within the profile/event. Encode both the UTC `startedAt` and UUID `id` of the last item in a versioned base64url cursor; query the next page with the lexicographic `<` predicate on that pair. Add `id DESC` to the supporting profile/event index. Reject malformed cursors with 400 naming `after`; equal timestamps must neither skip nor repeat records; `roster` served by `GET …/run-records/{id}/roster` with the record's `revision`-free ETag (`updated_at`).

### Client coordination

The apps half (`rebuild-foundation-pre-launch` in `tacticus-planner-apps`) deletes the goal-detail fan-outs and per-project goal fetches, adopts the new routes/error shape, and switches bulk status to one request. The regenerated OpenAPI artifact is the shared contract; the apps change regenerates its client from it, including `{ goal, order }`, matched archive selectors, compound run-record cursors/on-demand rosters, and writable-response Problem Details.

Merge/apply order is API then apps, but deployment is a coordinated cutover. Before merging the API half, pause or approval-gate its automatic staging promotion so an API merge can publish the image/OpenAPI artifact without exposing the new contract. Both paired PRs must pass CI/review and client contract tests before either version is promoted for use. Deploy the matched API and web artifacts together, or keep the environment inaccessible during the sequential rollout until both versions and a joint smoke test succeed. Do not expose the new error/run-record shapes to the old client. This pre-launch gate requires no legacy-contract shim.

## Risks / Trade-offs

- [`OwnsMany().ToJson()` of a nested collection inside `teams` (members, runDepths) at three levels] → identical depth already works for `player_data_snapshots.inventory`; covered by a Postgres integration test on plan round-trip.
- [Per-profile slot is stricter than per-project: a user who had the same unit in two projects at different Rank targets loses nothing (targets differ) but two identical in-flight goals in two projects are now one goal in two projects] → that is the intended model; V1 import de-duplicates accordingly.
- [Bigger goals list payload] → compression (from `optimize-api-handlers`) and `staleTime` (from `tune-client-query-caching`) make it a one-off per 5 min; measured in validation.
- [Error-shape change breaks every client error handler at once] → update the shared `ApiError` parser and all conflict/issue-code/current-state consumers in the apps half, and block promotion until the matched client passes contract tests.
- [Losing migration history] → nothing to preserve pre-launch; the archived changes remain the design record.

## Migration Plan

1. Pause or approval-gate automatic API staging promotion, then merge the API half to publish the new model, `InitialCreate`, routes, tests, image, and OpenAPI artifact. Keep the current contract serving until the paired client is ready; both halves require green CI and resolved review threads.
2. Local: Aspire `api-migrations` → **Drop Database** then start (auto-migrate). Staging/production: `tacticus-planner-infra` drop + recreate the PostgreSQL database (no data migration).
3. Merge the apps half with the regenerated client and verify the shared contracts. Promote matched API/web artifacts as one cutover, or keep the environment inaccessible until both deployments and joint smoke tests pass; only then reopen access and restore automatic promotion.
4. Docs: ADR 0001 → Superseded; `data.md` JSONB section rewritten; new ADR for jsonb aggregates with validator invariants.
Rollback: previous API image + previous database (snapshot taken before drop); acceptable because there are no users yet.

## Open Questions

- Whether `user_settings` should fold into `profiles.settings jsonb` (saves a table; costs nothing either way) — decide during spec authoring, does not affect other decisions.
- Default page size for run records (20 proposed) — product call, adjustable without contract change.
