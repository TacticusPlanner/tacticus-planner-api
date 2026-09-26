## 1. Contract and transaction

- [x] 1.1 Add `expectedGoalIds` and structured stale/last-membership responses to the existing project membership endpoint (keeping the members-with-`globalPriority` response), and replace the stale "…and priority ordering" summary text; verify generated OpenAPI and all request/response shapes.
- [x] 1.2 Compare reviewed/current sets under the project lock before writes and retain atomic slot/orphan validation; verify API tests for fresh, stale, unknown, slot conflict, orphan, and mixed valid/invalid batches.
- [x] 1.3 Add a concurrent-edit test where another membership change wins first; verify stale save rejects atomically. Add a test that a successful add/remove leaves every goal's `GlobalPriority` and the profile's `GoalOrderRevision` unchanged, and that a concurrent reorder does not make the membership save stale.

## 2. Pair and gates

- [ ] 2.1 Coordinate the same-named apps change and verify every `updateProjectGoals` caller sends the expected set and handles conflict responses before deploying either side.
- [x] 2.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, and `git diff --check`; verify all pass.
