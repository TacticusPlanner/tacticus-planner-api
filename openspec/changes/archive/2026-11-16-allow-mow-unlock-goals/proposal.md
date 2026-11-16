## Why

The apps' create sheet and Collection's bulk goal dialog both offer an Unlock goal for a Machine of War the account doesn't own, and the apps already track it (reached once the MoW is owned). The API rejects it with 400 "This goal type is not valid for the selected entity type.", so every unowned MoW fails, and the bulk dialog's Retry fails forever. The goal-target-model spec already describes Machine-of-War Unlock goals (its source-kind rules), so the code, not the spec, is out of line.

Companion apps work: bulk goal fixes in `tacticus-planner-apps` (no apps change is needed for this part).

## What Changes

- Unlock is valid for a Character or a Machine of War. Rank stays Character only.
- The catalog shard-source check (`IsUnlockEligible`) applies to characters only; a MoW has no shard nodes, so any MoW in the catalog the account doesn't own is eligible.
- An Unlock goal for a unit the account already owns is still rejected, for either entity type.
- Source-kind rules are unchanged: `Onslaught` and `Shop` stay rejected on MoW Unlock goals.
- V1 goal import is unchanged: it still never synthesizes an Unlock for a MoW.

## Capabilities

### Modified Capabilities

- `goal-target-model`: which entity types each goal type accepts.

## Impact

- `src/TacticusPlanner.Api/Features/Goals/GoalTargetValidationService.cs`.
- Tests: the MoW Unlock rejection test becomes an acceptance test; the MoW Unlock Shop-source test now sends a real MoW Unlock goal.
- No contract, OpenAPI, database or configuration changes.
