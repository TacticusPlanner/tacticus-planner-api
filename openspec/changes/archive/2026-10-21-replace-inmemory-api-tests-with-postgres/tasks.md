# Tasks

## 1. PostgreSQL HTTP test host

- [x] 1.1 Add `TacticusPlanner.Api.IntegrationTests` to the solution with centrally managed xUnit v3, MVC testing, Npgsql, and Testcontainers dependencies; verify locked restore and test discovery succeed.
- [x] 1.2 Implement the assembly-scoped disposable PostgreSQL container and per-factory database lifecycle, keeping production Npgsql pooling, execution strategy, metadata interceptors, and migration startup; verify an authenticated HTTP smoke test persists data in the migrated database, parallel factories cannot read each other's data, and disposal cleans up only owned databases.
- [x] 1.3 Relocate or share external-boundary fakes without introducing a dependency on the old test assembly; verify authentication variants, fake upstream responses, and recording analytics remain isolated, with no real upstream/analytics calls.
- [x] 1.4 Document the host lifecycle, Docker prerequisite, and new project command in README; verify missing Docker produces an actionable setup failure without skips or InMemory fallback.

## 2. Move database-backed coverage

- [x] 2.1 Record a before/after test inventory and move account/profile/settings, Tacticus integration/player sync, and V1 import database-backed tests into the new project; fix relational seed graphs and verify all original assertions and theory cases pass on PostgreSQL.
- [x] 2.2 Move goal/project/order and guild/raid database-backed API tests; verify the original behavioral cases pass with actual foreign keys, unique constraints, and JSON mappings, recording their new locations in the inventory.
- [x] 2.3 Move legendary-event plan/team/run and all remaining database-backed API tests, splitting mixed classes; verify the inventory accounts for every former database-backed case and pure/catalog tests still run independently without Docker.

## 3. Remove provider workarounds

- [x] 3.1 Remove non-relational bypasses from `ProjectGoalPlanningService`, `GuildRaidStatusService`, and `LegendaryEventPlanWriter`; add PostgreSQL HTTP regressions for concurrent goal mutations, guild persistence rollback on failure, and stale/concurrent legendary-event writes; verify existing conflict and ordering behavior remains intact.
- [x] 3.2 Replace the import execution-strategy simulation with real PostgreSQL import success and atomic rollback regressions, then remove `TrackingExecutionStrategyFactory`, `TrackingExecutionStrategy`, and `ImportedGoalExecutionStrategyGuard`; verify imports use the production strategy and failed work leaves no partial persisted data.
- [x] 3.3 Remove the obsolete InMemory factory/provider package and unused central version/lock entries; audit source and tests for equivalent provider fallbacks and remove any used only for test accommodation. Verify no `UseInMemoryDatabase`, EF InMemory dependency, or test-driven transaction/locking bypass remains, while legitimate configuration `AddInMemoryCollection` and external fakes remain functional.
- [x] 3.4 Update API AGENTS.md and README to describe the three test responsibilities and prohibit production persistence switches for tests; verify documented Docker-free and integration commands run as written.

## 4. Measurement and optimization handoff

- [x] 4.1 Add the test-host-only command interceptor, request correlation header, and `CommandsFor(response)` helper using the design's counting convention; verify nonzero SQL for `GET me/goals`, isolation between simultaneous requests, exclusion of migration/seed/assertion SQL, and coverage of sync/async reader/scalar/nonquery execution without duplicate counting.
- [x] 4.2 Reconcile `optimize-api-handlers` proposal/design/tasks to consume the new integration host and measurement convention, declaring this prerequisite and retaining its performance targets and pending task status; verify both changes pass OpenSpec validation and no obsolete InMemory measurement instruction remains.

## 5. CI and integration verification

- [x] 5.1 Ensure `_build-api.yml` executes the new project and existing persistence suite on Docker-enabled runners and retains failure test results; record suite duration and verify the CI job budget accommodates it without filtering/skipping integration coverage.
- [x] 5.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; verify the complete suite passes, the moved-test inventory is reconciled, and generated OpenAPI has no contract changes.
- [x] 5.3 Run `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .`; verify the container build succeeds after dependency cleanup.

## Workflow follow-up

- Wait for latest-commit CI and CodeRabbit review, address actionable threads, and archive after repository review requirements are satisfied.
- Resume `optimize-api-handlers` after this prerequisite is complete; record its baseline before changing handlers.
- Account for all other active API changes before reporting completion; no companion apps implementation is required.
