## 1. API

- [x] 1.1 Read V1 material-goal wire fields into `V1Goal`
- [x] 1.2 Resolve a type-2 goal whose `character` is a Mow as a Mow Ascension goal
- [x] 1.3 Translate type 6 into an owned Upgrade goal; merge same-owner material goals
- [x] 1.4 Report type 7 as `manual_step_required` with material and linked V1 goal ids
- [x] 1.5 Add `upgradeId` / `linkedSourceGoalIds` to `V1GoalOutcome` and the OpenAPI artifact
- [x] 1.6 Endpoint tests for each case

## 2. Client (tacticus-planner-apps)

- [x] 2.1 Map the new outcome codes and show the pre-farm manual step after import
