# Proposal

## Why

Database-backed API tests currently replace PostgreSQL with EF Core InMemory, forcing production code to bypass transactions and locks and preventing meaningful SQL-command measurements. Establish real PostgreSQL endpoint integration coverage before applying `optimize-api-handlers`, so tests exercise the persistence behavior deployed in production.

## What Changes

- Create `tests/TacticusPlanner.Api.IntegrationTests` with a Testcontainers PostgreSQL fixture and an HTTP test host using the production Npgsql registration, migrations, interceptors, and execution strategy.
- Move all database-backed API tests and their fixtures out of `TacticusPlanner.Api.Tests`, preserving their assertions and data isolation. Keep pure tests and database-independent catalog HTTP tests Docker-free.
- Remove EF Core InMemory dependencies and production provider switches introduced to satisfy tests, including transaction/locking bypasses in project mutations, guild raid persistence, and legendary-event plan writes. Retain fake authentication, upstream clients, and analytics at external boundaries.
- Replace the test-only execution-strategy substitute with tests of real PostgreSQL transactions, rollback, and concurrent requests.
- Add request-correlated SQL-command instrumentation in the integration host to unblock the optimization measurement gate; endpoint performance ceilings remain owned by `optimize-api-handlers`.
- Wire the project into solution/CI execution and document prerequisites, isolation, troubleshooting, and test ownership. Update repository guidance to describe the new split.

## Capabilities

### New Capabilities

None. This is test infrastructure and a behavior-preserving implementation cleanup; `skip_specs: true` deliberately omits delta specs.

### Modified Capabilities

None. Existing endpoint contracts and domain requirements remain authoritative.

## Impact

API repository only: test projects, the three production provider branches, package references, solution, CI, README, and AGENTS.md. Existing `TacticusPlanner.Persistence.IntegrationTests` retains migration, relational-constraint, and direct persistence coverage. No schema migration, catalog changes, or API contract change is required; no companion apps change is required.

Implement this prerequisite before `optimize-api-handlers` and before the schema rebuild. Its implementation includes reconciling the optimization change's measurement locations with the new project, without marking any optimization task complete or changing its performance targets.
