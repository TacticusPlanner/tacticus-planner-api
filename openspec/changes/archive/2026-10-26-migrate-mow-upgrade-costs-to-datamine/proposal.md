# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 6 migrates the existing catalog onto the pinned datamine, one dataset per change, cost/progression tables first. `mow-upgrade-costs` is a V1 port (`Data/mow-upgrade-costs.json`) of the game's `units.abilityUpgradeCostsMoW`, now available as the datamine slice `gameconfig/units/abilityUpgradeCostsMoW.json`.

## What Changes

- **Source.** The ladder is read from `DatamineFiles.Open("gameconfig/units/abilityUpgradeCostsMoW.json")`. Each row maps `gold` → `gold`, `dust` → `salvage` (absent = 0), `abilityToken{Rarity}` → `badges`, `itemAscensionResource_{Rarity}` → `forgeBadges` (absent = none), `machinesOfWarToken` → `components`. `Data/mow-upgrade-costs.json` is deleted and leaves `GameCatalogDatasets.Required`; the datamine file joins `files.txt`.
- **Served shape and values unchanged.** All 59 rows are identical after the key mapping, so no manifest hash moves. No `SchemaVersion` bump, no apps change.
- **Validation.** A row without `gold`, `machinesOfWarToken` or exactly one `abilityToken*`, with more than one `itemAscensionResource_*`, or with any other key fails startup naming the dataset and row. The hand-kept file had no such check.

## Capabilities

### New Capabilities

- `mow-upgrade-costs-dataset`: the served MoW ability cost ladder, its datamine source and key mapping, and its load-time checks.

### Modified Capabilities

None.

## Impact

- Companion apps change: **none** (nothing served changes).
- `src/TacticusPlanner.GameCatalog`: `Denormalization/MowDenormalizer.cs` (`ReadDatamineMowUpgradeCosts`), `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, `Models/Mows.cs` (comment); deleted `Data/mow-upgrade-costs.json`; new datamine copy `gameconfig/units/abilityUpgradeCostsMoW.json`.
- Tests: new `MowUpgradeCostTests`; manifest snapshot unchanged.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
