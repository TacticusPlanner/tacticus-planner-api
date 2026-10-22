# Proposal

## Why

The project is pre-launch with no data to preserve, so this is the one cheap moment to fix the structural costs the 2026-10-20 audit found: a goal↔project slot rule enforced through five denormalized columns on `project_goals` that every mutation must resync under a lock chain; a seven-table Legendary-Event plan aggregate that is only ever read and written whole; an append-only `goals.events` history nothing reads; `accounts`/`profiles` split 1:1 and joined on every request; immutable child rows carrying audit timestamps; a goals list that strips the `config` the client then re-fetches one goal at a time; five mutation routes for one goal resource; three incompatible error shapes; and 17 migrations whose raw SQL the EF model does not know about. Companion apps change: `rebuild-foundation-pre-launch` in `tacticus-planner-apps`; this API half applies first.

Decisions already taken with the owner: in-flight goal slots become **per profile** (not per project); `goals.events` is dropped; the LE plan becomes one row with jsonb children and its row-level CHECKs move to the application validator; `accounts` merges into `profiles` (one profile per account stays the rule; splitting again later is a pre-processor + table-split change, nothing below `ProfileId` moves). General rule adopted: database constraints only for uniqueness, foreign keys and nullability; range/enum invariants live in validators.

## What Changes

**Schema (all BREAKING; database recreated from a new single `InitialCreate` migration, existing migrations and their migration-specific integration tests deleted):**
- `accounts` merged into `profiles` (`issuer`, `subject`, `last_seen_at`; unique `(issuer, subject)`). `tacticus_integrations`, `user_settings`, `player_data_overrides`, `player_data_snapshots` keep their 1:1 rows keyed by profile id.
- `goals`: drop `events`. The in-flight slot rule moves to the profile (not the project) and is enforced only in the app, under the profile lock: no slot indexes and no derived `rank_target_key` column. `project_goals` becomes `(project_id, goal_id, created_at)`.
- `legendary_event_plans` absorbs `teams` (with members, objectives, run depths), `run_progress` and `objective_annotations` as three jsonb columns; the six child tables are dropped. Served plan shape is unchanged.
- `legendary_event_run_records` absorbs `roster_snapshots` as a `roster` jsonb column; the table is dropped.
- `guild_raid_attacks` absorbs `guild_raid_attack_units` as a `units` jsonb column; the table is dropped.
- Dead columns dropped: `player_data_overrides.battle_result_overrides`, `player_data_snapshots.tacticus_last_updated_on`. `created_at`/`updated_at` removed from child/immutable rows (`project_goals`, `guild_raid_attacks`). Redundant `ix_projects_profile_id` dropped. CHECK constraints on `goals.status/goal_type/entity_type` and `projects.status/type/target_kind` replaced by validator enums (text columns stay).
- `goals.depends_on` entries are removed from dependents when the referenced goal is deleted.

**API contract (BREAKING):**
- `GET me/goals` returns full goal detail (config, snapshot, `projectIds`, `revision`, `globalPriority`); `GET me/goals/{id}` keeps the same shape. `GET me/projects` returns each project's `goalIds`. `GET me/projects/{id}/goals` is removed.
- Goal mutations collapse to `PUT me/goals/{id}` (today's `/edit` semantics), `POST me/goals/status` (bulk `{goalIds, status}`, replacing `POST me/goals/{id}/status` and `POST me/projects/{id}/goals/status`), `PUT me/goals/order`, `DELETE me/goals/{id}`. `PUT me/goals/{id}/target`, `/projects`, the plain `PUT`, and the single-goal `POST me/goals` are removed; `POST me/goals` takes the combined request.
- Slot conflicts name the conflicting goal only (no project list). `GoalEvent`, `GoalTargetSnapshot` and the `events` field leave the contract.
- `POST tacticus-integration/player-sync` moves to `POST me/player-data/sync`. `POST tacticus-api-key/validate` is removed (unused). The `guilds/me[...]` routes stay as they are (four guild-raid requirements name them; renaming buys nothing).
- `GET me/legendary-event-plans/{eventId}/run-records` is cursor-paged (`startedAt DESC, id DESC`, cursor containing both values) and excludes the roster; `GET .../run-records/{recordId}/roster` serves it on demand.
- Every writable error response is RFC 7807 `ProblemDetails` with an `issueCode` extension; the six bespoke 409 records and FastEndpoints' `ErrorResponse` shape are retired. Conflict bodies keep carrying the current resource state under `current`.
- Catalog dataset URLs in the manifest carry the complete unencoded-envelope hash (including release metadata); the manifest payload hash stays separate; dataset responses are `Cache-Control: public, max-age=31536000, immutable`.
- Every mutation returns the updated resource; combined edit returns `{ goal, order }`, with order `{ revision, goalIds }` when priority is supplied and null otherwise. Goal/project lists use matching archive selectors for membership ids. Creation names its request-level boolean `startPaused` (default false; explicit null is invalid).

## Capabilities

### New Capabilities
- `goal-collection-read`: the full-detail goals list, `projectIds` on goals and `goalIds` on projects as the single source for goal↔project membership.
- `goal-bulk-status`: one request transitions many goals, atomically, with per-goal conflict reporting.
- `api-error-contract`: the single `ProblemDetails` + `issueCode` error shape and the `current`-state convention on 409.

### Modified Capabilities
- `project-goal-slots`: "In-flight uniqueness is scoped to project" → scoped to profile; "Conflicts identify affected projects and goals" → identify the conflicting goal; "Concurrent mutations in one project serialize without failing" → per profile; "Database enforces concurrent slot occupancy" → indexes on `goals`.
- `project-bulk-membership-replacement`: membership replacement no longer performs slot checks (membership cannot create a slot conflict); snapshot/revision and no-reorder requirements unchanged.
- `goal-combined-edit`: "Existing endpoints keep their behavior" removed — the combined edit **is** `PUT me/goals/{id}`; section validation and conflict semantics unchanged.
- `goal-target-editing`: "Concurrent edits and target history are explicit" → revision semantics kept, event history requirement removed.
- `goal-lifecycle-status`: "Existing goal statuses are not rewritten" removed (no data preserved); bulk status path added to "Project membership operations do not change a goal's status" family.
- `global-goal-priority`: "Existing orders migrate deterministically" removed; order semantics unchanged.
- `goal-target-model`: "Existing goals migrate to an equivalent acquisition-source set" removed; model unchanged.
- `legendary-event-plans`: "One revision per plan guards every mutation" — the 409 body becomes the unified error shape (`issueCode` `legendaryEventPlanStale`, current plan under `current`). Team validation requirements already describe 400s, not constraints, so they are unchanged by the storage move.
- `legendary-event-run-records`: "Run records are readable per event" → paged, roster excluded, plus an on-demand roster read; "stores … a roster snapshot" → stores the roster inline, never null.
- `roster-snapshots`: the snapshot is no longer a separately identified entity; both requirements restate the content rule against the inline column.

Checked and deliberately not modified (requirements are storage-agnostic): `guild-raid-current-status` (attack units inline is invisible to the contract), `analytics-identity` ("account" remains the merged row), `player-data-sync` (never mentions battle-result overrides).

## Impact

- `src/TacticusPlanner.Domain` (Account removed, Goal/LE/GuildRaid/RosterSnapshot aggregates), `src/TacticusPlanner.Persistence` (configurations, `Migrations/` replaced by one `InitialCreate` incl. the raw deferrable `UNIQUE (profile_id, global_priority)`), `src/TacticusPlanner.Api/Features/*` (routes, mappers, `GoalConflictDetection`, `ProjectGoalPlanningService`, `LegendaryEventTeamMutations`, `GlobalExceptionHandler`), `artifacts/openapi`.
- Tests: delete `AddGlobalGoalPriorityMigrationTests`, `GoalAcquisitionSourcesMigrationTests`, `LegendaryEventPlansMigrationPostgresTests`, `LegendaryEventRunRecordsMigrationPostgresTests`, `RemoveActiveProjectMigrationTests`, `RemoveLevelGoalsMigrationTests`; rewrite slot/concurrency Postgres tests for the profile-scoped indexes; endpoint tests follow the new routes.
- `tacticus-planner-docs`: supersede ADR 0001 (single `public` schema with prefixes is the decision), amend `architecture/data.md` JSONB section to match ADR 0002/0007 and this change, add an ADR for "aggregate-as-jsonb with validator-enforced invariants".
- `tacticus-planner-infra`: staging/production database is dropped and recreated (no data migration).
- Apply after the completed PostgreSQL API-test prerequisite and `optimize-api-handlers`, preserving the established PostgreSQL host and measurement convention.
- Apply/merge API before apps to generate the contract artifact, but gate automatic staging promotion before API merge. Publish the new error, paging/roster, and goal contracts only with the matching client: coordinated deployment, or an inaccessible environment until both halves deploy and joint smoke tests pass. No legacy compatibility shim is needed pre-launch.
