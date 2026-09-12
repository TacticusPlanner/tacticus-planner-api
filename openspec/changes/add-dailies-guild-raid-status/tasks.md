## 1. Status Contract and Projection

- [x] 1.1 Add the id-only current-status response/state records and verify serialization tests cover active, no-active-season, nullable timing, nullable prime HP, and freshness fields.
- [x] 1.2 Implement the pure catalog-backed current-position projector without a subsequent-boss preview and verify focused tests cover no hits, a living boss, a defeated boss, tier advancement, configured loops, and repeated unit ids from older loops.
- [x] 1.3 Implement boss/prime HP and proportional modifier-threshold projection and verify focused tests cover catalog HP fallback, active/inactive modifiers, and exact null `activationRemainingHp`/`active` values when prime HP is unresolved.
- [x] 1.4 Resolve `endsAt` only from an explicit active Guild Raid event occurrence and verify tests cover matching and absent occurrence cases without recurrence inference.

## 2. Persistence, Refresh, and Endpoint

- [x] 2.1 Add the guild-scoped Guild Raid sync state plus season, attack, and attack-unit persistence model and EF Core migration, with guild/season ownership, cascade behavior, deterministic unique/idempotent attack identity, a `(GuildRaidSeasonId, TacticusUserIdHash, CompletedAt)` composite index, and a `LastAttemptedAt` column on the sync-state row for cooldown gating; verify PostgreSQL integration tests cover active/no-active state, constraints, repeated writes, and indexed current-user ordering.
- [x] 2.2 Implement transactional upstream normalization and a per-guild single-flight forced-refresh service gated by a one-minute cooldown keyed on `LastAttemptedAt` (updated on every attempt, success or failure); verify concurrency/time-controlled tests cover cooldown reuse, forced-refresh coalescing, overlapping idempotent writes, transient persisted stale fallback, rejected-credential behavior with retained data, and process-restart reuse.
- [x] 2.3 Make the status read path a pure query with no upstream access, returning conflict when the guild has no persisted observation yet; verify tests cover long-stale reads served without an upstream call and the never-observed conflict case.
- [x] 2.4 Add a no-tracking current-user attack repository projection that accepts season id plus the caller's existing Tacticus user-id hash, selects only required columns, and restricts optional unit loading to matching attack ids; verify relational query tests prove another member's attacks/units are not materialized and the SQL predicates match the composite index.
- [x] 2.5 Add the authenticated `GET /api/v1/guilds/me/raid-status` (pure read) and `POST /api/v1/guilds/me/raid-status/refresh` (cooldown-gated forced refresh) endpoints with guild-readiness/token lookup and upstream error mapping on refresh; verify endpoint tests cover unprovisioned, unregistered, never-synced, never-observed, active, no-season, rejected, unavailable, cooldown-reuse, and persisted stale-fallback responses without leaking credentials or raw attacks.
- [x] 2.6 Register the persistence/status services and verify the API test host resolves both endpoints and one request uses the fake upstream client plus the current catalog snapshot.

## 3. Contract and Repository Gates

- [x] 3.1 Build the API, inspect the regenerated `artifacts/openapi` diff for the exact endpoint/query/response contract (including the new `POST /refresh` route), and confirm it matches the companion `tacticus-planner-apps` change before applying that side.
- [x] 3.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it exits successfully.
- [x] 3.3 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify it exits successfully.
- [x] 3.4 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify all tests pass.
