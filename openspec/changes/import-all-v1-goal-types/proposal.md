## Why

The V1 goal import rejects two of V1's seven goal types outright (6 "Specific Upgrade Material" and 7 "Pre-farm Material for Goals") and silently fails a third case: V1 stores a Machine of War Ascension goal's unit under `character` (V1's goal dialog never writes `unitId`), so the import looks it up among characters and reports it as an unknown unit. Users moving from V1 lose those goals with no path forward.

Companion apps change: `import-all-v1-goal-types` in `tacticus-planner-apps` (outcome codes, the pre-farm manual-step prompt). This API half applies first; the new outcome fields are additive.

## What Changes

- Type 2 (Ascend) whose `character` is a Machine of War resolves to a Mow Ascension goal.
- Type 6 (Upgrade Material) imports as an Upgrade goal. V1's goal has no unit, so the import picks an owner: the first unit (V1 priority order) among the profile's own imported unit goals that needs the material, else the first roster unit that does. Material goals given the same owner fold into that unit's one Upgrade goal, one target per material with quantities summed.
- Type 7 (Pre-farm) has no V2 goal type. It reports a new `manual_step_required` skip carrying the material and the V1 ids of the goals it was linked to, so the client can prompt the user to re-create it.
- New outcome codes: `unknown_upgrade`, `no_upgrade_owner`, `upgrade_goal_occupied`, `manual_step_required`. `unsupported_goal_type` now only covers type values V1 never had.
- `V1GoalOutcome` gains optional `upgradeId` and `linkedSourceGoalIds`.
- Each import run that creates goals puts them in a new project of its own, named "Import <date, time> UTC", instead of the default project.

## Capabilities

### Modified Capabilities

- `v1-goal-import`: material goal translation, Mow Ascension resolution, Upgrade merge rule, new outcome codes, a per-import project.

## Impact

- `Features/V1Import`: `V1Goal` DTO (V1 wire fields `upgradeMaterialId`, `upgradeMaterialQuantity`, `preFarmGoalIds`), `V1GoalImportService`, `V1GoalOutcome`.
- `artifacts/openapi`: two optional properties on `V1GoalOutcome`.
- Tests: `V1GoalImportEndpointTests`.
- No database, migration, configuration or infra changes.
