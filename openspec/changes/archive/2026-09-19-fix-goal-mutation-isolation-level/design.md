## Context

See proposal.md — Why. The mechanism matters for the fix, so it is spelled
out here.

`ProjectGoalPlanningService.ExecuteLockedMutationAsync` is the single seam
every project-touching goal mutation passes through. It currently:

1. opens a transaction at `IsolationLevel.Serializable`,
2. takes `SELECT 1 FROM projects WHERE id = {0} FOR UPDATE` on each target
   project, in ascending id order,
3. runs the caller's mutation, which ends by calling `NormalizeAsync` —
   renumbering `Priority` on **every** membership row in the project — and
   committing.

PostgreSQL takes one transaction-wide snapshot at the first statement. Here
that first statement is the `FOR UPDATE` itself, and the snapshot is acquired
*before* the statement blocks:

```
T1 (create goal A)                      T2 (create goal B)
BEGIN SERIALIZABLE
                                        BEGIN SERIALIZABLE
SELECT 1 ... FOR UPDATE
  <- snapshot S1 taken
                                        SELECT 1 ... FOR UPDATE
                                          <- snapshot S2 taken, THEN blocks
INSERT goal + membership
UPDATE project_goals SET priority = ..
  (all N rows)
COMMIT
                                        unblocks cleanly: the projects row
                                          was only LOCKED, never UPDATEd, so
                                          no serialization error is raised
                                          here
                                        SELECT project_goals -> sees S2,
                                          which is STALE
                                        UPDATE project_goals -> targets rows
                                          updated by a committed concurrent
                                          transaction  =>  40001
```

Two consequences, and the second is the one that matters:

- The abort is unconditional whenever another mutation committed first,
  because `NormalizeAsync` writes every membership row in the project.
- If it somehow did *not* abort, the mutation would be **wrong**:
  `LoadProjectMembershipsAsync` runs after the lock is acquired but returns
  T2's pre-lock snapshot, so it cannot see T1's new goal. The `40001` is the
  only thing currently preventing a lost update.

The retry wrapper cannot rescue this. `CreateExecutionStrategy()` does return
a retrying strategy — the Aspire Npgsql integration enables retry by default
and nothing here disables it — and Npgsql does classify `40001` as transient.
But `strategy.ExecuteAsync` re-runs the delegate against the **same**
`DbContext` without resetting the change tracker. The second attempt re-adds
a membership whose key is already tracked and throws
`InvalidOperationException` ("another instance with the same key value is
already being tracked"), which is not a `PostgresException` and so is not
retried again — it propagates as HTTP 500. A third hazard: the HTTP response
is written *inside* the retried delegate, so a retry after a partial write
would corrupt the response.

## Goals / Non-Goals

**Goals:**

- Concurrent goal mutations in one project queue and all commit.
- Each mutation reads post-commit state, so `NormalizeAsync` and
  `GetNextPriorityAsync` operate on current data rather than a stale
  snapshot.
- A racing same-slot create yields the already-specified structured 409.
- Concurrency behavior is covered by a test that runs against real
  PostgreSQL.

**Non-Goals:**

- Changing the ordering data model. `Priority` stays a contiguous `int`
  renumbered by `NormalizeAsync`.
- Making the retry wrapper functional.
- Changing any endpoint contract, schema, or client code.
- Reducing the number of round trips the V1 import makes. That belongs to
  `rewrite-v1-goal-import`.

## Decisions

### Use READ COMMITTED and keep the row lock as the mutual-exclusion mechanism

Change `IsolationLevel.Serializable` to `IsolationLevel.ReadCommitted` in
`ExecuteLockedMutationAsync`. Under `READ COMMITTED` each statement takes a
fresh snapshot, so a waiter that acquires the lock then reads the rows
committed by its predecessor and writes without conflict.

This is safe because **every** decision-relevant read already happens after
the lock is held: the slot pre-check, the next-priority lookup, and the
membership load. There is no read-then-act window outside the lock that the
transaction-wide snapshot was protecting. Locks are still taken in ascending
project id order, so multi-project mutations cannot deadlock. The in-flight
slot invariant additionally has a database backstop — the partial unique
index on project membership — and project metadata has its own optimistic
concurrency token, so neither depends on the isolation level.

*Alternatives considered:*

- **Drop `NormalizeAsync` from the write path; normalize lazily or only on an
  explicit reorder.** This shrinks the write set, but does not remove the
  conflict class on its own: two creates in one project still both write
  `project_goals` and, under `SERIALIZABLE`, still both abort. It also
  changes observable behavior — a new goal would stop slotting next to its
  unit's existing goals automatically, which `project-unit-ordering`
  requires.
- **Sparse priorities (increments of 1000) or fractional / rank-string sort
  keys.** Both remove renumbering cost. Neither is needed: contiguity is
  enforced nowhere in the database (only a range check), no client renders a
  priority number, and every consumer treats it as a comparison key. A
  project holds tens of goals, so roughly thirty `UPDATE`s under a held lock
  is not a cost worth a new ordering model, a migration, and a new
  comparator.
- **A linked-list / `after_goal_id` model.** Worst fit: ordered reads would
  need a recursive CTE, the existing `ORDER BY priority` list path would have
  to be rewritten, and broken or cyclic chains become a new corruption class.
- **Fix the retry instead.** Requires hoisting the response writes out of the
  retried delegate and clearing the change tracker per attempt. But several
  call sites load and mutate entities *before* entering the delegate and use
  them inside it, so a blanket `ChangeTracker.Clear()` breaks them and each
  endpoint needs restructuring. Strictly more work than not conflicting, and
  it buys retry latency where this decision buys zero conflicts.
- **Fix only the client (submit sequentially).** Makes the V1 import green
  while leaving the defect fully armed for every other concurrent path.
  Rejected as a fix; sequential submission is still desirable for its own
  reasons and belongs to `rewrite-v1-goal-import`.

### Document the retry wrapper rather than remove it

`strategy.ExecuteAsync` stays, because EF Core refuses a user-initiated
transaction under a retrying strategy unless the transaction is created
inside the strategy's delegate. It gets a comment stating plainly that it is
*not* a working retry (change tracker is not reset between attempts; the HTTP
response is written inside the delegate) and that the design must therefore
avoid retryable failures by construction rather than rely on absorbing them.
The upgrade path is named in the comment for whoever needs it later.

### Cover concurrency with a PostgreSQL-backed test

The API test suite builds its host on EF Core InMemory, and
`ExecuteLockedMutationAsync` deliberately no-ops the transaction and the row
lock on a non-relational provider. The entire mechanism under discussion is
therefore invisible to those tests — which is why this shipped. The new
coverage goes in the persistence integration test project, which already has
a Testcontainers-backed PostgreSQL harness, and fires several concurrent
locked mutations that each insert a membership and normalize. It fails before
the fix and passes after, which makes it the smallest check that catches a
regression.

## Risks / Trade-offs

- **Mutations in one project are strictly serial.** → Not a regression: they
  were already serial, they just aborted instead of waiting. Wall-clock time
  for a batch of N mutations is unchanged; the difference is that N of them
  now succeed instead of one.
- **Lock hold time grows with project size**, because `NormalizeAsync` runs
  inside the lock. → Bounded by goals-per-project, which is tens. If a
  project ever grows large enough for this to matter, the fix is to stop
  renumbering on the write path — a separate change with its own behavior
  implications, noted in Alternatives above.
- **A long-running mutation now makes others wait rather than fail fast.** →
  Acceptable; the alternative is the current behavior, which is a 500.
- **The broken-retry pattern exists elsewhere** (the guild raid status
  service uses the same `strategy.ExecuteAsync` shape). → Out of scope here
  and called out in tasks as a follow-up note, not silently fixed.
- **`READ COMMITTED` would be unsafe if a future change moved a
  decision-relevant read outside the lock.** → The comment added at the
  isolation-level line states the dependency explicitly, so the constraint is
  visible at the point where it could be broken.

## Migration Plan

No schema change, no data migration, no deployment sequencing. The change is
behavioral within a single transaction helper and takes effect on deploy.
Rollback is reverting the isolation level.
