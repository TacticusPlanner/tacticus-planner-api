# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 7 adds catalog datasets for game data the apps still hardcode.
The apps keep the XP curve and XP book values (`goal-farming/lib/level-xp-cost.ts`), the XP level each rank-up slot
needs (`rank-additional-target.ts`), the per-rarity level, ability and rank caps (`game-domain/progression.ts`), star
ranges and rarity factors (`rarity-from-stars.ts`, `ability-text.ts`) and the MoW mythic levels. The game ships them in
`units/xpLevels.json`, `consumables.json`, `units/upgradeSlots.json`, `units/heroProgressionSteps.json` and
`units/heroProgressionStepsMoW.json`.

## What Changes

- **New dataset `progression-rules`.** A one-element list holding, under the game's names: `xpLevels`, `xpBooks`,
  `upgradeSlots` (six slot XP levels per rank, Stone1 to Adamantine2) and `heroProgressionSteps` /
  `mowProgressionSteps` (per ascension step: progression, rarity, stars, caps, stat multipliers, HRE shard cost).
- **Served at** `/api/v1/game-catalog/progression-rules`, listed in the manifest. `SchemaVersion` 12 → 13. No existing
  dataset hash moves.
- **Validation.** A non-increasing XP curve, XP books that aren't one per rarity, rank rows that aren't 20 × 6, an
  unknown max rank, or a ladder that doesn't walk the catalog progression order fails startup.
- **Test fix.** `DatamineFilesTests` used `units/xpLevels.json` as its never-listed path; it now uses
  `achievements.json`.

## Capabilities

### New Capabilities

- `progression-rules-dataset`: the served progression rules, their datamine sources and load-time checks.

### Modified Capabilities

None.

## Impact

- Companion apps change: roadmap step 7 apps PRs "XP and levels from `progression-rules`" and "Caps and stars from
  `progression-rules`". `progressionOrder` and the rank list stay TypeScript unions, checked against this dataset by a
  contract test. The game's Mythic max rank is Adamantine3 while the apps and the API's `GameDomain` cap at
  Adamantine2 (nothing reaches A3 yet); the apps change decides, this change serves the game's value.
- `src/TacticusPlanner.GameCatalog`: `Models/ProgressionRules.cs`, `Denormalization/ProgressionRulesDenormalizer.cs`,
  `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, `Models/GameCatalogSnapshot.cs`,
  `Models/GameCatalogRelease.cs`; datamine copies `consumables.json`, `units/upgradeSlots.json`, `units/xpLevels.json`.
- `src/TacticusPlanner.Api`: one served endpoint; OpenAPI artifact.
- Tests: new `ProgressionRulesDenormalizerTests`; manifest snapshot gains one dataset and `schemaVersion` 13.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no infra change.
