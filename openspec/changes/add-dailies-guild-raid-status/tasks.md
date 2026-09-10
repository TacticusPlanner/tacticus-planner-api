## 1. Status Contract and Projection

- [ ] 1.1 Add the id-only current-status response/state records and verify serialization tests cover active, no-active-season, nullable timing, nullable prime HP, and freshness fields.
- [ ] 1.2 Implement the pure catalog-backed current/next-position projector and verify focused tests cover no hits, a living boss, a defeated boss, tier advancement, configured loops, and repeated unit ids from older loops.
- [ ] 1.3 Implement boss/prime HP and proportional modifier-threshold projection and verify focused tests cover catalog HP fallback, active/inactive modifiers, and unresolved prime HP.
- [ ] 1.4 Resolve `endsAt` only from an explicit active Guild Raid event occurrence and verify tests cover matching and absent occurrence cases without recurrence inference.

## 2. Refresh and Endpoint

- [ ] 2.1 Implement the bounded per-guild five-minute fresh/30-minute stale cache and single-flight refresh service, and verify concurrency/time-controlled tests cover fresh reuse, forced refresh, coalescing, stale fallback, and expiration.
- [ ] 2.2 Add the authenticated `GET /api/v1/guilds/me/raid-status` endpoint with guild-readiness/token lookup and upstream error mapping, and verify endpoint tests cover unprovisioned, unregistered, never-synced, active, no-season, rejected, unavailable, and stale-fallback responses without leaking credentials or raw hits.
- [ ] 2.3 Register the status services and verify the API test host resolves the endpoint and one request uses the fake upstream client plus the current catalog snapshot.

## 3. Contract and Repository Gates

- [ ] 3.1 Build the API, inspect the regenerated `artifacts/openapi` diff for the exact endpoint/query/response contract, and confirm it matches the companion `tacticus-planner-apps` change before applying that side.
- [ ] 3.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it exits successfully.
- [ ] 3.3 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify it exits successfully.
- [ ] 3.4 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify all tests pass.
