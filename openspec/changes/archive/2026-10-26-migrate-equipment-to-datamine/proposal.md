# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 5 is the pilot of the datamine migration: move the `equipment` dataset off its hand-kept raw files (`Data/equipment/equipment-{type}.json`, ported from V1, plus `Data/equipment-upgrade-costs.json`) onto the pinned datamine copy of the game's `gameconfig/items.json` that `add-datamine-source` committed. It is small and maps directly, so it proves the pull, the binding and the manifest-hash check end to end before the larger datasets move.

## What Changes

- **Source.** `EquipmentDenormalizer` reads `DatamineFiles.Open("gameconfig/items.json")` (keyed by item id). The five `Data/equipment/*.json` files and `Data/equipment-upgrade-costs.json` are deleted, and their keys leave `GameCatalogDatasets` (`EquipmentTypes`, `EquipmentUpgradeCosts`).
- **Served shape unchanged.** Same fields, same values, same 217 ids. Five game stat names are mapped back to the names the dataset has always served (`critDmg`→`critDamage`, `critDmgBonus`→`critDamageBonus`, `blockDmg`→`blockDamage`, `blockDmgBonus`→`blockDamageBonus`, `fixedArmor`→`armor`). `upgradeLevels` comes from each item level's own `goldCost`/`dustCost`/`mythicDustCost` instead of a per-rarity table; for every item it equals the retired table's ladder for its rarity. No `SchemaVersion` bump, no apps change (roadmap rule: keep the shape or rename, never both).
- **Order.** Served items are ordered by item type, then id (ordinal). The retired files were sorted by id except that relics were appended at the end of `equipment-crit.json` and `equipment-booster-block.json`, so 18 relics move position. The `equipment` manifest hash moves for that reason only; no other dataset hash moves.
- **Allowlist.** `gameconfig/globalValues.json` is pruned from `Data/datamine/files.txt` (its `itemAscensionCosts` turned out not to be needed: the per-level costs are on each item). The pin stays `v1.43.157.2989`.

## Capabilities

### New Capabilities

- `equipment-dataset`: the served `equipment` dataset's source, field mapping, upgrade ladder and order.

### Modified Capabilities

- `game-catalog-datamine-source`: the refresh scenario no longer names `globalValues.json`, which is no longer allowlisted.

## Impact

- Companion apps change: **none** (served shape unchanged; the hash move makes clients refetch identical items in a new order).
- `src/TacticusPlanner.GameCatalog`: `Models/Equipment.cs` (`DatamineItem`, `DatamineItemLevel`; `UpgradeLevels` on the raw record; `GameCatalogEquipmentUpgradeCost` removed), `Denormalization/EquipmentDenormalizer.cs`, `GameCatalogLoader.cs`, `Models/GameCatalogSnapshot.cs`, `Models/GameCatalogDatasets.cs`; deleted `Data/equipment/**`, `Data/equipment-upgrade-costs.json`, `Data/datamine/raw/gameconfig/globalValues.json`.
- Tests: new `EquipmentDenormalizerTests`; manifest snapshot promoted (equipment hash only).
- Docs: `game-catalog-data` skill (both mirrors); data-mines `docs/roadmap.md` marks step 5 done once this is archived.
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
