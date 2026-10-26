## 1. Contract and validation

- [ ] 1.1 Add `Notes` (string?) to `CombinedGoalSpec` and `Position` (int?) to `CreateCombinedGoalsRequest`; the validator applies `MaximumLength(GoalValidation.MaxNotesLength)` to each spec's notes and `position >= 1` — verified by the notes-too-long and position-0 integration tests
- [ ] 1.2 Regenerate `artifacts/openapi` by building and confirm `notes` and `position` appear as nullable fields; the apps `polish-goal-dialogs` change consumes the regenerated types

## 2. Behaviour

- [ ] 2.1 Save each spec's notes on its goal — verified by an integration test creating two goals, one with notes and one without
- [ ] 2.2 Insert the created batch at `position` in request order under the profile lock, shifting later goals; reject `position > in-flight + 1` with a 400 naming `position`; null appends — verified by integration tests for position 1, null, 0 and count+2

## 3. Gates

- [ ] 3.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build` pass
