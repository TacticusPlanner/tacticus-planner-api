# Tasks

## 1. Measurement gate

- [ ] 1.1 Verify prerequisite `replace-inmemory-api-tests-with-postgres` is complete; use `tests/TacticusPlanner.Api.IntegrationTests/PlannerApiFactory.cs` and its existing `CommandsFor(response)` helper. Verify a nonzero `GET me/goals` count with sync/async reader/scalar/nonquery attempts including retries and pre-processors, excluding migration/seed/assertion SQL outside requests and transaction begin/commit; verify concurrent-request isolation before recording baselines.
- [ ] 1.2 Record the current ceilings in new `TacticusPlanner.Api.IntegrationTests` tests for `GET me/goals`, `POST me/goals/combined` (3 specs), `POST me/goals/{id}/status`, `PUT me/goals/order`, `GET guilds/me/raid-status`, `POST tacticus-integration/player-sync` with the pre-change numbers as the asserted maximum; verify they pass before any handler change

## 2. HTTP transport

- [ ] 2.1 Enable Brotli+gzip response compression (`EnableForHttps = true`, JSON mime types) in `orchestration/TacticusPlanner.ServiceDefaults/Extensions.cs`; verify an `Api.Tests` request with `Accept-Encoding: br` returns `Content-Encoding: br` + `Vary: Accept-Encoding` and decodes to the same JSON
- [ ] 2.2 In `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` set `ETag` from `snapshot.DatasetHashes[datasetKey]` via `ETagHelper`, `Cache-Control: public, max-age=0, must-revalidate`, and return 304 on matching `If-None-Match`; verify tests for 200+ETag, 304, and ETag-matches-manifest-hash on two datasets
- [ ] 2.3 Add the fixed-window rate limiter (`Microsoft.AspNetCore.RateLimiting`, partition by profile id, fallback `sub`) with policies `upstream-fast` (10/min) and `upstream-slow` (6/h) bound to configuration keys `RateLimiting:UpstreamFastPerMinute` / `RateLimiting:UpstreamSlowPerHour`; apply to the six endpoints named in the spec; verify tests for 429 + `Retry-After`, per-profile isolation, and that 429 leaves `TacticusSyncLastAttemptedAt` untouched
- [ ] 2.4 Document the two configuration keys in `README.md`'s settings table and verify the OpenAPI artifact lists `304` on dataset endpoints and `429` on the six limited endpoints after `dotnet build`

## 3. Goal validation and ordering

- [ ] 3.1 Spike: in `tests/TacticusPlanner.Persistence.IntegrationTests` add a test proving EF Core translates `snapshot.Characters.FirstOrDefault(u => u.UnitId == id)` / `Mows` to SQL on Postgres; if it fails, record the fallback (project only `Characters`+`Mows` columns) in design.md and use it in 3.2
- [ ] 3.2 Change `Features/Goals/GoalTargetValidationService.ValidateAsync` to project only the targeted unit(s) per 3.1 and add an overload that accepts an already-resolved unit; verify the existing goal validation tests pass and the command-count ceiling for `POST me/goals` drops by one full-row load
- [ ] 3.3 `CreateCombinedGoalsEndpoint`: resolve the roster once per request and pass units into validation; verify `POST me/goals/combined` command count no longer grows with the number of specs (test with 1 vs 3 specs)
- [ ] 3.4 `GoalOrderService.CompleteAsync`/`LoadProfileAsync`: reuse the tracked profile from the unit of work (drop `FirstAsync`+`ReloadAsync`), check density from a `(Id, GlobalPriority, Status)` projection and load full rows only when renumbering; remove the redundant profile selects in `ProjectGoalPlanningService.ExecuteLockedMutationAsync` and `ReadyGuildResolver`; verify `GlobalGoalOrderEndpointTests` + Postgres ordering tests pass and the ceilings for status/delete/create drop accordingly
- [ ] 3.5 Lower the asserted ceilings from 1.2 to the new numbers and verify the suite is green

## 4. Guild and player-data reads

- [ ] 4.1 `GuildSyncService`: replace the all-profiles load with `Where(p => hashes.Contains(p.TacticusUserIdHash))` (`IgnoreQueryFilters`) and delete the stale comment; verify `SyncMyGuildEndpointTests`/`RegisterGuildEndpointTests` pass and a Postgres test shows the generated SQL filters by hash
- [ ] 4.2 `GuildRaidStatusService.LoadRetainedAsync`: push `EncounterType == Boss` and `CompletedAt >= cutoff` into the query (latest per encounter in SQL or a bounded ordered take), keep `GuildRaidStatusProjector` pure; verify `GuildRaidStatusProjectorTests` unchanged and `GetMyGuildRaidStatusEndpointTests` pass with a seeded season of 1000 attacks under the new ceiling
- [ ] 4.3 `PlayerDataSyncService`: read `ChunkHashes` + metadata by projection and write changed chunks with `ExecuteUpdateAsync` (revision/synced_at/updated_at included), keep the insert path; verify `PlayerDataSyncEndpointTests` pass, including the "only changed chunks rewritten" and "unchanged configHash skips persistence" cases
- [ ] 4.4 `GetCurrentUserEndpoint`: write `LastSeenAt` only when the stored value is older than one hour; verify a test issues two `GET me` within an hour and observes one write

## 5. Cleanup

- [ ] 5.1 Delete `Features/Guilds/GuildRaidAttackRepository.cs`, its DI registration and the test-only records; delete the scalar `PlayerDataManifestBuilder.Build` overload; replace `Guid.NewGuid()` with `Guid.CreateVersion7()` in `LegendaryEventPlanWriter`, `LegendaryEventTeamMutations`, `LegendaryEventRunRecordRecorder`; verify build has no unused-member warnings and LE tests pass

## 6. Verification

- [ ] 6.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build` all green; `docker build -f src/TacticusPlanner.Api/Dockerfile .` succeeds
- [ ] 6.2 Through the Aspire stack, capture a browser network export of first-visit catalog load before/after and record total transferred bytes in this file (expected ≥ 70 % reduction); record the per-endpoint command counts before/after from 1.2/3.5

## Workflow follow-up

- No companion apps change is required; note in the PR that the catalog client may later add `If-None-Match` on dataset fetches.
- Archive after CI and review threads are resolved.
