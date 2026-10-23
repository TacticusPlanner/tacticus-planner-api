## 1. API

- [x] 1.1 Read V1 material-goal wire fields into `V1Goal`
- [x] 1.2 Resolve a type-2 goal whose `character` is a Mow as a Mow Ascension goal
- [x] 1.3 Turn type 7 into an Upgrade goal per linked unit and type 6 into one on a suggested unit, at their V1 priority; report the leftovers as `manual_step_required`
- [x] 1.4 Put each import's created goals in a new "V1 import · <date>" project, and return its id
- [x] 1.5 Add `upgradeId` / `linkedSourceGoalIds` / `upgradeQuantity` to `V1GoalOutcome` and the OpenAPI artifact
- [x] 1.6 Endpoint tests for each case

## 2. Client (tacticus-planner-apps)

- [x] 2.1 Map the new outcome codes and show the pre-farm manual step after import
