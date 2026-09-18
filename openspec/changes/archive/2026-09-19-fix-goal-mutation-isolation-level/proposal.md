## Why

Every goal mutation that touches a project runs inside a `SERIALIZABLE`
transaction that also takes a row lock on the project. The row lock alone
already provides complete mutual exclusion, so the isolation level adds
nothing except turning *waiting* into *aborting*: a second concurrent
mutation acquires its snapshot before the lock blocks, then fails with
PostgreSQL `40001 serialization_failure` the moment it writes. The retry
that should absorb that failure is itself broken, so the request surfaces as
HTTP 500.

The result is that two concurrent goal mutations in one project cannot both
succeed — two browser tabs, a reorder racing a create, or the V1 goal import
submitting several units at once. The V1 import is the reported symptom:
users see most of their goals fail with "2 created, 17 failed", and retrying
succeeds for only one or two more each time.

## What Changes

- Concurrent goal mutations targeting **distinct** slots in one project all
  succeed. They serialize on the existing project row lock and each observes
  the previously committed state instead of aborting on a stale snapshot.
- Concurrent goal mutations targeting the **same** slot continue to produce
  the documented structured HTTP 409 conflict — which is what
  `project-goal-slots` already requires, and what the current 500 violates.
- The goal-mutation transaction runs at `READ COMMITTED`. This is not a
  weakening: every ordering- and conflict-relevant read is issued *after* the
  project row lock is held, so per-statement snapshots are strictly more
  correct here than a transaction-wide snapshot taken before the lock.
- The non-functional retry wrapper around the locked mutation is documented
  as non-functional, so a future change does not mistake it for working
  resilience.
- Concurrency behavior gains PostgreSQL-backed test coverage. The existing
  API test suite runs on EF Core InMemory, where the whole lock path is
  short-circuited — which is why this defect shipped unnoticed.

No API contract change, no schema change, no migration, no client change.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `project-goal-slots`: adds a requirement that concurrent mutations against
  distinct slots within one project all commit rather than one succeeding and
  the rest failing. The existing slot-conflict requirements are unchanged in
  intent; this change makes the already-specified 409-on-race behavior
  actually reachable.

## Impact

- `src/TacticusPlanner.Api/Features/Projects/ProjectGoalPlanningService.cs` —
  the shared locked-mutation helper. All five goal-mutation call sites route
  through it: single goal creation, combined goal creation, goal status
  transitions, goal-side membership replacement, and project-side membership
  replacement.
- `tests/TacticusPlanner.Persistence.IntegrationTests/` — new PostgreSQL-backed
  concurrency coverage alongside the existing project-goal Postgres tests.
- No companion `tacticus-planner-apps` change. This is server-only and
  requires no coordinated client work; the V1 import rewrite
  (`rewrite-v1-goal-import`, paired across both repos) depends on this change
  landing first but does not share a contract surface with it.
