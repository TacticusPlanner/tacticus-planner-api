## 1. Widen upgrade-target relevance

- [x] 1.1 Add a recursive crafted-recipe expansion to `GameCatalogGoalLookups` that reduces
      a set of upgrade ids to their base (non-craftable) ingredients, guarding against a
      recipe cycle, and verify a unit test covers a nested crafted recipe
- [x] 1.2 Return the union of the literal ids and their decomposed base ids from
      `CharacterRelevantUpgradeIds` and verify a unit test asserts a ladder-only crafted id
      and a recipe-only base id are both members
- [x] 1.3 Apply the same union to `MowRelevantUpgradeIds` and verify a unit test covers a
      base ingredient reachable only through a MoW ability recipe

## 2. Validation coverage

- [x] 2.1 Add a `GoalTargetValidationService` test creating an Upgrade goal whose target is
      a decomposed base ingredient absent from the unit's literal ladder, and verify it is
      accepted
- [x] 2.2 Add a test asserting an unrelated upgrade id is still rejected, and verify the
      existing literal-ladder target tests still pass

## 3. Verify

- [x] 3.1 Run `dotnet test` for the affected projects and verify the full suite passes
