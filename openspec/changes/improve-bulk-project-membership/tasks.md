## 1. Contract and transaction

- [ ] 1.1 Reconcile canonical order/storage with `establish-global-goal-priority`, then add `expectedGoalIds` and structured stale/last-membership responses to the existing project membership endpoint; verify generated OpenAPI and all request/response shapes.
- [ ] 1.2 Compare reviewed/current sets under the project lock before writes and retain atomic slot/orphan validation; verify API tests for fresh, stale, unknown, slot conflict, orphan, and mixed valid/invalid batches.
- [ ] 1.3 Add a concurrent-edit test where another membership change wins first; verify stale save rejects atomically and global priority remains unchanged.

## 2. Pair and gates

- [ ] 2.1 Coordinate the same-named apps change and verify every `updateProjectGoals` caller sends the expected set and handles conflict responses before deploying either side.
- [ ] 2.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, and `git diff --check`; verify all pass.
