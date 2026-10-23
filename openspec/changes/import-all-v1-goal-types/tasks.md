## 1. API

- [x] 1.1 Read V1 material-goal wire fields into `V1Goal`
- [x] 1.2 Resolve a type-2 goal whose `character` is a Mow as a Mow Ascension goal
- [x] 1.3 Report types 6 and 7 as `manual_step_required` with material, quantity, linked V1 goal ids and a suggested unit
- [x] 1.4 Put each import's created goals in a new "Import <date, time> UTC" project
- [x] 1.5 Add `upgradeId` / `linkedSourceGoalIds` / `upgradeQuantity` to `V1GoalOutcome` and the OpenAPI artifact
- [x] 1.6 Endpoint tests for each case

## 2. Client (tacticus-planner-apps)

- [x] 2.1 Map the new outcome codes and show the pre-farm manual step after import
