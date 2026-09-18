## Why

An Upgrade goal's targets are validated against `CharacterRelevantUpgradeIds` /
`MowRelevantUpgradeIds`, which are the **literal** upgrade ids printed on the unit's
rank-up ladder (or a MoW's ability recipes). Those ids are largely *crafted* upgrades,
which a player cannot farm directly — the farmable thing is the crafted upgrade's base
ingredients. Every one of the 117 characters has base ingredients that its ladder never
names literally (28-62 ids per character), so the API rejects exactly the materials a
player would actually farm toward, with "An upgrade target is not relevant to the
selected unit's own requirements."

This blocks the web client's companion fix (`tacticus-planner-apps` change
`fix-upgrade-goal-target-picker`), which decomposes a rank range's crafted upgrades into
their base ingredients and offers those as Upgrade goal targets. The API half must apply
first, or the client would offer targets the server refuses.

## What Changes

- `CharacterRelevantUpgradeIds` and `MowRelevantUpgradeIds` expand crafted upgrades
  through their recipes recursively, and return the **union** of the literal ladder/recipe
  ids and the decomposed base-upgrade ids.
- Accepting the union (rather than replacing the literal set) keeps every currently valid
  target valid — no persisted goal becomes unverifiable, and no migration is needed.
- No wire-format, schema, or endpoint change: only the set of `upgradeId` values the
  existing validation accepts widens.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `goal-target-model`: adds a requirement pinning which upgrade ids an Upgrade goal may
  target — today's relevance rule is implemented but unspecified, and this change widens
  it to include recursively decomposed base ingredients.

## Impact

- `src/TacticusPlanner.GameCatalog/GameCatalogGoalLookups.cs` — both relevance lookups.
- `src/TacticusPlanner.Api/Features/Goals/GoalTargetValidationService.cs` — caller only;
  its logic is unchanged.
- Companion client change: `tacticus-planner-apps` / `fix-upgrade-goal-target-picker`.
- Purely widening: no breaking change, no migration, no persisted-data rewrite.
