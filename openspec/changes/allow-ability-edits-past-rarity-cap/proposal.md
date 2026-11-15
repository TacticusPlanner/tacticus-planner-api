## Why

UserJot request "Allow Editing Goals Beyond Rarity Caps": an owner with a Legendary Machine of War's Ability goal (imported from V1, 36 to 50) tried to edit the target to 60 to see how long it would take, and the edit was rejected with "Ability targets cannot exceed the current rarity cap of 50." V1 allowed goals past the unit's rarity. Creation already reaches such targets by adding an Ascension prerequisite, but an existing goal has no way to gain one, so its target is stuck at the live cap.

## What Changes

- A target edit (`PUT /goals/{id}/target`) bounds an Ability target by the highest ability level (60) instead of the unit's current rarity cap. Ascension dependencies no longer matter to an edit.
- Creation is unchanged: single and combined creation still validate Ability targets against the live rarity cap, lifted by depended-upon Ascension goals.
- Rank, Ascension and Upgrade targets were never bound by the unit's rarity, on create or edit; a test now pins that for Rank and Ascension.
- Companion apps change (`allow-ability-goals-past-rarity-cap` in `tacticus-planner-apps`): the creation form keeps offering an Ability goal once a unit's abilities reach its rarity cap. The edit form already offers levels up to 60, and the goals page already marks the part of a target past the unit's rarity as "capped by rarity".

## Capabilities

### Modified Capabilities

- `goal-target-editing`: edited Ability targets are not bound by the rarity cap.
- `goal-dependencies`: the Ascension cap floor applies to creation only.

## Impact

- `src/TacticusPlanner.Api/Features/Goals/GoalTargetValidationService.cs`, `GoalEditing.cs`, `AscensionFloor.cs` (doc only).
- Tests: three new cases in `GoalAbilityCapEffectiveProgressionTests`.
- No contract, OpenAPI, database or configuration changes.
