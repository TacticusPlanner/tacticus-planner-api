## 1. Domain and persistence

- [x] 1.1 Add `RankRange`, `ActiveRange`, `PassiveRange` (`Start`/`End`) to `UpgradeTarget` in `GoalConfig.cs`
- [x] 1.2 Add the same fields to `GoalTargetSnapshot` and include them in `From`/`SameTargetAs`
- [x] 1.3 Map the new members in `GoalConfiguration.cs` (`OwnsOne` under `Upgrade`, and under the event `PreviousTarget`/`NewTarget`)
- [x] 1.4 `dotnet ef migrations add AddUpgradeGoalRanges` (model snapshot only; confirm it emits no row-rewriting SQL)

## 2. API contract

- [x] 2.1 Extend `UpgradeTargetRequest` and `UpgradeTargetResponse` with the three optional range groups
- [x] 2.2 Map them in `GoalMapper.cs` (create, response, event snapshot) and `GoalEditing.cs` (replace-as-a-whole on edit)
- [x] 2.3 Add request-shape validation (group fully present or absent) to the create and `UpdateGoalTargetEndpoint` validators
- [x] 2.4 Verify the regenerated `artifacts/openapi` artifact and note the shape for the apps change

## 3. Validation rules

- [x] 3.1 In `GoalTargetValidationService` reject the wrong group for the entity type, `end <= start`, ranks outside the ladder, ability levels beyond the track's ladder
- [x] 3.2 Confirm the ability-track ladder bound against the catalog (recipe row count) for a real MoW

## 4. Tests

- [x] 4.1 `UpgradeGoalsEndpointTests`: Character with `rankRange` (`ultraApothecary`, Stone2→Stone4, `upgArmC002`×5) round-trips
- [x] 4.2 MoW (`astraOrdnanceBattery`) with `activeRange` only; with both tracks; wrong-kind group rejected
- [x] 4.3 Invalid bounds rejected on create and on edit
- [x] 4.4 `GoalTargetEditEndpointTests`: range replaced, range cleared by omission, `TargetChanged` event records old/new range
- [x] 4.5 Pre-existing Upgrade goal without ranges reads back with null groups

## 5. Hand-off

- [x] 5.1 Run the full API test suite and lint; apply this change before the apps change
