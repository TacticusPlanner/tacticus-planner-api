## Why

An Upgrade goal stores only `targets: [{ upgradeId, quantity }]`. The create form already lets a
user pick a rank range (Character) to choose which materials to offer, but throws the range away,
so the planner cannot tell when an Upgrade goal and a Rank/Ability goal on the same unit want the
same upgrade slots and counts those materials twice. Persisting the range lets the client
de-duplicate overlapping demand.

Companion change: `tacticus-planner-apps` → `fix-upgrade-goal-farming-need` (same name; the API
half applies first). That change also fixes the missing Upgrade branch in the client's farming
calculation, which is why Upgrade goals are currently absent from Today's raids and the Schedule.

## What Changes

- `config.upgrade` gains three optional, independent range groups:
  `rankRange { start, end }` (Character only), `activeRange { start, end }` and
  `passiveRange { start, end }` (Machine of War only, one per ability track).
- Create validates the ranges for the owning unit; absent ranges stay valid and mean "no overlap
  de-duplication" (every existing goal).
- `PUT /me/goals/{id}/target` for an Upgrade goal accepts the same optional ranges alongside
  `targets`, replaced as a whole; the `TargetChanged` event snapshot records them.
- Responses (goal detail, event previous/new target) return the ranges.
- EF model change for the new JSON-owned members, with its migration (snapshot only, no data
  rewrite).
- No **BREAKING** change: all new fields are optional and nullable.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `goal-target-model`: Upgrade goal config carries optional per-unit-kind progression ranges and
  their validation.
- `goal-target-editing`: the Upgrade target group may replace its ranges, and the history event
  records them.

## Impact

- `TacticusPlanner.Domain/Goals/GoalConfig.cs`, `GoalTargetSnapshot.cs`
- `TacticusPlanner.Api/Features/Goals/` — `CreateGoalEndpoint.cs`, `UpdateGoalTargetEndpoint.cs`,
  `GoalEditing.cs`, `GoalMapper.cs`, `GoalTargetValidationService.cs`
- `TacticusPlanner.Persistence` — `GoalConfiguration.cs` + a new migration
- Regenerated `artifacts/openapi`; the apps change consumes the new request/response fields.
- V1 import is unaffected: V1 upgrade-material goals are still reported as unsupported
  (`v1-goal-import`).
