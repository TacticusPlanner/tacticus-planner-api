## 1. Reproduce the defect

- [ ] 1.1 Add a PostgreSQL-backed concurrency test alongside the existing
      project-goal Postgres integration tests that fires several concurrent
      `ExecuteLockedMutationAsync` calls against one project, each inserting a
      distinct membership and calling `NormalizeAsync`; verify it fails on
      the current code with `40001` / a tracked-entity error, confirming the
      test actually exercises the lock path rather than the InMemory no-op
- [ ] 1.2 Extend that test with a racing same-slot case and verify it
      currently produces an unhandled error rather than the documented
      structured slot-conflict response

## 2. Fix the isolation level

- [ ] 2.1 Change `IsolationLevel.Serializable` to
      `IsolationLevel.ReadCommitted` in
      `ProjectGoalPlanningService.ExecuteLockedMutationAsync`, and verify the
      tests from 1.1 and 1.2 now pass
- [ ] 2.2 Add a comment at the isolation-level line recording that the
      `SELECT ... FOR UPDATE` on the project row is the mutual-exclusion
      mechanism and that `READ COMMITTED` is *required* so a waiter reads
      post-commit state instead of aborting on a pre-lock snapshot; state the
      standing constraint that every conflict- and ordering-relevant read must
      stay inside the lock. Verify by review that the constraint is stated at
      the line where it could be broken
- [ ] 2.3 Add a comment at the `CreateExecutionStrategy()` /
      `strategy.ExecuteAsync` lines recording that this is not a working retry
      (the change tracker is not reset between attempts, and the HTTP response
      is written inside the retried delegate), that it exists only because EF
      Core requires a user-initiated transaction to be created inside the
      strategy delegate, and naming the upgrade path. Verify by review

## 3. Confirm no regression across call sites

- [ ] 3.1 Run the existing API test suite and confirm single goal creation,
      combined goal creation, goal status transitions, goal-side membership
      replacement, and project-side membership replacement all still pass
      unchanged (`dotnet test TacticusPlanner.slnx -c Release --no-build`)
- [ ] 3.2 Add a Postgres-backed assertion that after concurrent mutations the
      project's in-flight priorities form a contiguous sequence starting at 1
      with no duplicates and no gaps, covering the ordering scenario in the
      spec delta
- [ ] 3.3 Verify by review that no read used for a conflict or ordering
      decision was moved outside the lock by this change, and that no
      endpoint newly depends on transaction-wide snapshot semantics

## 4. Live verification

- [ ] 4.1 Start the stack through Aspire
      (`aspire run --project orchestration/TacticusPlanner.AppHost/TacticusPlanner.AppHost.csproj`),
      wait for `api` and `api-migrations` to report healthy, then submit
      several concurrent goal-creation requests for distinct units to one
      project and verify every one returns success with contiguous priorities
- [ ] 4.2 Against the same running stack, submit two concurrent creations for
      the same unit and goal type and verify one succeeds and the other
      returns the structured HTTP 409 slot-conflict body, with no 500 in the
      API logs

## 5. Repository gates

- [ ] 5.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
      reports no changes
- [ ] 5.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` succeeds
- [ ] 5.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` passes,
      including the new PostgreSQL-backed concurrency tests

## 6. Follow-ups (not in this change)

- [ ] 6.1 File a tracking issue noting that `GuildRaidStatusService` uses the
      same non-functional `strategy.ExecuteAsync` retry shape, so the pattern
      is recorded rather than silently left in place. Verified by the issue
      link being present in the change before archive
