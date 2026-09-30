## 1. Extract shared mutation logic

- [x] 1.1 Move the target-edit core of `UpdateGoalTargetEndpoint` into a transaction-scoped method returning a result union (no `Send.*`), and make the endpoint a wrapper; verify the existing target endpoint tests pass unchanged
- [x] 1.2 Do the same for the details core of `UpdateGoalEndpoint`; verify its existing tests pass unchanged
- [x] 1.3 Do the same for the membership core of `UpdateGoalProjectsEndpoint`; verify its existing tests pass unchanged
- [x] 1.4 Add `GoalOrderService.MoveToPositionAsync` (validates 1..N and that the goal holds a position; same move as a drag reorder) and verify unit tests for the move-up, move-down, unchanged, out-of-range, and no-position cases from the spec

## 2. Combined edit endpoint

- [x] 2.1 Add `EditGoalEndpoint` (`PUT me/goals/{goalId}/edit`) with request/response records, validator, OpenAPI summary and response declarations, running the four cores in target, details, projects, priority order inside one `ExecuteLockedMutationAsync` with the restart-on-drift loop and a single `SaveChangesAsync`; verify it appears in the regenerated OpenAPI artifact
- [x] 2.2 Map every non-applied result to the existing 400/409 bodies and roll back the transaction; verify tests for invalid target, membership slot conflict against the new target, stale goal revision with valid notes, stale order revision with a valid target, and 404
- [x] 2.3 Add success and rollback tests: all four sections together, each section alone, empty request, absent sections untouched, one revision bump, and a failing last section leaving earlier sections unwritten
- [x] 2.4 Add a concurrency test with two overlapping edits (goals sharing two projects) and a membership change during an edit; verify no deadlock and correct validation against reloaded memberships

## 3. Contract and hand-off

- [x] 3.1 Regenerate the OpenAPI artifact and verify the diff contains only the new endpoint and its schemas
- [x] 3.2 Run the repository build, format, and full test commands from `AGENTS.md` and `git diff --check`; verify all pass
- [ ] 3.3 Local-stack check: with the Aspire stack running, call `PUT me/goals/{id}/edit` with a combined body and verify the goal and order update together, and a deliberately conflicting body changes nothing
