# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 7 adds catalog datasets for game data the apps still hardcode, then
deletes the apps copies. The apps keep a faction → alliance map (`packages/game-domain/src/alliance.ts` and the faction
records) and a pierce-ratio table copied from the wiki (`packages/game-domain/src/pierce-ratio.ts`). The game ships both
in `units.factions` and `units.damageProfiles`, now available as the datamine slices `gameconfig/units/factions.json`
and `gameconfig/units/damageProfiles.json`.

## What Changes

- **New dataset `factions`.** One record per game faction, read from `gameconfig/units/factions.json`: `id`, `alliance`
  (the faction's single alliance trait), `unlockDateUtc` (the game's `unlockTimestamp`) and `collectionFaction`
  (absent = false). Ordered by unlock time, then id.
- **New dataset `damage-types`.** One record per damage type, read from `gameconfig/units/damageProfiles.json`: `id`,
  `piercingRatio` (the game's percentage, 0-100) and `traits` (absent = empty). Ordered by id.
- **Served at** `/api/v1/game-catalog/factions` and `/api/v1/game-catalog/damage-types`, listed in the manifest.
  `SchemaVersion` 10 → 11 (new served datasets). No existing dataset hash moves.
- **Validation.** A faction without exactly one alliance trait (Imperial, Chaos, Xenos) or an unlock time, or a damage
  type without a piercing ratio of 0-100, fails startup. Every character's, MoW's and non-object NPC's faction must be a
  served faction with the same alliance, and every damage type a character or NPC deals must be a served damage type.

## Capabilities

### New Capabilities

- `factions-dataset`: the served factions, their datamine source and load-time checks.
- `damage-types-dataset`: the served damage types, their datamine source and load-time checks.

### Modified Capabilities

None.

## Impact

- Companion apps change: roadmap step 7 apps PR "Use `factions` and `damage-types`" replaces `game-domain`'s
  faction → alliance lookup and `pierce-ratio.ts` (piercing ratio / 100). It ships after the step 7 API PRs; today's
  apps skip the unknown dataset keys, so this change needs no apps change to deploy.
- `src/TacticusPlanner.GameCatalog`: `Models/Factions.cs`, `Denormalization/FactionDenormalizer.cs`,
  `Validation/FactionValidation.cs`, `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`,
  `Models/GameCatalogSnapshot.cs`, `Models/GameCatalogRelease.cs`; new datamine copies.
- `src/TacticusPlanner.Api`: two served endpoints; OpenAPI artifact.
- Tests: new `FactionDenormalizerTests`; manifest snapshot gains two datasets and `schemaVersion` 11.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no infra change.
