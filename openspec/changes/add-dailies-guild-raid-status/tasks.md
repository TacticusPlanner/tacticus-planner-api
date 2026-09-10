## 1. Status Contract and Projection

- [ ] 1.1 Add the id-only current-status response/state records and verify serialization tests cover active, no-active-season, nullable timing, nullable prime HP, and freshness fields.
- [ ] 1.2 Implement the pure catalog-backed current-position projector without a subsequent-boss preview and verify focused tests cover no hits, a living boss, a defeated boss, tier advancement, configured loops, and repeated unit ids from older loops.
- [ ] 1.3 Implement boss/prime HP and proportional modifier-threshold projection and verify focused tests cover catalog HP fallback, active/inactive modifiers, and unresolved prime HP.
- [ ] 1.4 Resolve `endsAt` only from an explicit active Guild Raid event occurrence and verify tests cover matching and absent occurrence cases without recurrence inference.

## 2. Persistence, Refresh, and Endpoint

- [ ] 2.1 Add the guild-scoped Guild Raid sync state plus season, hit, and hit-unit persistence model and EF Core migration, with guild/season ownership, cascade behavior, lookup indexes, and deterministic unique/idempotent hit identity; verify PostgreSQL integration tests cover active/no-active state, constraints, and repeated writes.
- [ ] 2.2 Implement transactional upstream normalization and the per-guild five-minute fresh persisted-observation/single-flight refresh service; verify concurrency/time-controlled tests cover fresh reuse, forced refresh, coalescing, overlapping idempotent writes, persisted stale fallback, and process-restart reuse.
- [ ] 2.3 Add the authenticated `GET /api/v1/guilds/me/raid-status` endpoint with guild-readiness/token lookup and upstream error mapping, and verify endpoint tests cover unprovisioned, unregistered, never-synced, active, no-season, rejected, unavailable, and persisted stale-fallback responses without leaking credentials or raw hits.
- [ ] 2.4 Register the persistence/status services and verify the API test host resolves the endpoint and one request uses the fake upstream client plus the current catalog snapshot.

## 3. Contract and Repository Gates

- [ ] 3.1 Build the API, inspect the regenerated `artifacts/openapi` diff for the exact endpoint/query/response contract, and confirm it matches the companion `tacticus-planner-apps` change before applying that side.
- [ ] 3.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it exits successfully.
- [ ] 3.3 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify it exits successfully.
- [ ] 3.4 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify all tests pass.
