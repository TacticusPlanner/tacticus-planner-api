# Design

## Context

See proposal.md for motivation. `PlannerApiFactory` removes the pooled production context, registers `UseInMemoryDatabase`, and replaces the execution strategy with `TrackingExecutionStrategyFactory` plus an import guard. `ProjectGoalPlanningService`, `GuildRaidStatusService`, and `LegendaryEventPlanWriter` contain `IsRelational` branches that skip relational work. The existing persistence integration project already uses Testcontainers PostgreSQL for migrations and direct service tests. CI's reusable `_build-api.yml` runs the whole solution on an Ubuntu runner with Docker within a 15-minute job budget.

## Goals / Non-Goals

**Goals:** exercise real SQL, constraints, transactions, locks, and production context configuration through HTTP; preserve existing behavioral coverage; isolate tests; support trustworthy request command counts.

**Non-Goals:** handler optimizations, schema redesign, new endpoint behavior, live upstream/authentication calls, or wholesale consolidation of existing persistence tests. No catalog raw data, denormalization, manifest, or snapshot content changes. No migration or backfill. No companion apps change or shared contract change.

## Decisions

1. **Dedicated API integration project.** Move every test using `PlannerApiFactory` or otherwise requiring a database into `TacticusPlanner.Api.IntegrationTests`. Split mixed classes as needed. Keep pure projection/validation tests and genuinely database-independent catalog HTTP tests in `Api.Tests`. Keep existing migration/constraint tests in `Persistence.IntegrationTests`. Extending the old InMemory project would obscure its Docker requirement; placing all HTTP tests into the persistence project would obscure ownership. Record moved and retained test cases, including theory cases, to detect lost coverage.

2. **Real provider, isolated database.** Share one disposable PostgreSQL container across the new test assembly using xUnit v3 assembly fixture lifetime. Match the AppHost PostgreSQL major version and existing repository container conventions during implementation. Allocate a uniquely named database per API factory, retaining the old factory's isolation boundary even for concurrent tests. Supply its connection string before host startup, retain the production pooled Npgsql context and metadata interceptors, and run the existing migration hosted service to completion before sending requests. Use real migrations, never `EnsureCreated`. Dispose the host and its connections before deleting only that fixture-owned database; dispose the container at assembly teardown. Do not reset shared tables or wrap the whole HTTP test in an ambient rollback transaction: separate request connections must observe real commits and locks. Missing Docker fails integration setup clearly, with no skip or alternate-provider fallback.

3. **Keep external fakes, remove database simulation.** Relocate fake Tacticus/V1 clients, test claims authentication, and recording analytics with their consumers; isolate mutable fake state per factory. Continue clearing real analytics configuration. In-process HTTP TestServer and `AddInMemoryCollection` configuration remain valid: neither replaces PostgreSQL. Remove `TrackingExecutionStrategyFactory`, `TrackingExecutionStrategy`, and `ImportedGoalExecutionStrategyGuard` after real V1-import regression coverage exists. Use the production execution strategy; do not disable retries or interceptors merely to make tests pass.

4. **One production persistence path.** Remove the three observed provider bypasses and any equivalent test-only fallbacks discovered by the repository audit. Always execute the existing PostgreSQL transaction/locking path, preserving isolation levels, lock order, conflict responses, and rollback behavior. Do not rewrite transaction algorithms here. Repair seed fixtures to satisfy actual foreign keys, uniqueness, JSON mapping, and deferred constraints; never weaken production constraints or existing behavior assertions to accommodate fixtures. A newly exposed production defect that requires different behavior must be surfaced separately.

5. **Request-correlated measurement.** Register a test-only `DbCommandInterceptor` with the integration host's existing context options, preserving production interceptors and pooling. Count synchronous and asynchronous reader, scalar, and nonquery execution attempts once per execution, including retries. Use request-scoped observation through `IHttpContextAccessor`/`HttpContext.TraceIdentifier`; keep the counter thread-safe and factory-owned. A test-only response header exposes the request identifier to `CommandsFor(response)`. Include pre-processors and handler SQL; exclude migration, seeding, and assertion queries outside the request. Transaction begin/commit are not SQL-command counts; record this convention. Verify a nonzero goals GET count, two concurrent requests with separate counters, and exclusion of fixture setup. Keep headers/instrumentation out of production registrations.

6. **Dependency handoff.** Apply this change before `optimize-api-handlers`. During implementation update that change's proposal, design decision 8, and measurement tasks to reference the new factory/project and counting convention. Its baseline ceilings and before/after measurements still belong to the optimization change. Preserve every optimization acceptance target and leave its tasks unchecked until actually verified there. The schema rebuild follows these changes and must use the new test locations.

7. **CI and guidance.** Add the project to the solution and use centrally managed dependencies, removing the EF InMemory package/version when no consumer remains. CI must discover and execute both integration projects and publish useful test results on failure. Measure total suite time against the existing timeout and adjust resources/timeout from evidence if needed; do not silently filter out tests. Update README and AGENTS.md with explicit Docker-free and Docker-backed commands and the rule against test-driven provider switches.

## Risks / Trade-offs

- [Database creation and migrations cost more than InMemory] -> share container lifetime, keep per-factory isolation, and record local/CI runtime before choosing further optimizations.
- [Existing seeds rely on absent relational constraints] -> preserve assertion intent and repair relationally invalid seed graphs, including account/profile setup.
- [Parallel fixtures leak data or pooled connections] -> unique fixture database names, teardown ownership checks, and concurrent isolation tests.
- [Real PostgreSQL exposes hidden defects] -> report behavior changes separately rather than adding new provider switches or weakening assertions.
- [Removing the import guard loses regression coverage] -> demonstrate import under the real execution strategy and verify failed multi-write work leaves no partial persisted state.

## Migration Plan

Add the new host and fixture; migrate database-backed tests with a coverage inventory; add real transaction and measurement regressions; remove provider workarounds and obsolete dependencies; reconcile optimization artifacts; update CI/docs and run full gates. Merge as one coherent change. No database deployment migration is involved; rollback is a code/test change revert. This proposal creates planning artifacts only.
