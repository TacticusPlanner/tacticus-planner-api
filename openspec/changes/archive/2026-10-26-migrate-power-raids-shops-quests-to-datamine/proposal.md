# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 6 migrates the catalog onto the pinned datamine, in batches as the maintainer asked. This change moves the next four datasets, whose V1 files were already slices of the game config: `unit-power`, `raid-bosses`, `shops` and `mythic-quests`.

## What Changes

- **unit-power.** Read from `gameconfig/units/` `heroProgressionSteps`, `heroProgressionStepsMoW`, `damageProfileModifiers`, `traitPowerModifiers`, `abilityPowerModifiers`, `abilityPowerCurve` and the power fields of `lineup.json`. Every value equals V1's slice, so the hash does not move.
- **raid-bosses.** Read from `globalconfig/guildBoss/` rotation, primarchs, modifiers, unitSets and season configs. The game's two unit sets are identical and equal V1's merged files (V1 dropped only `visualId`, which is not served), so the hash does not move. The loader fails if the sets ever diverge.
- **shops.** Read the `guild`, `guildWars`, `elderShop` and `crusadeShop` merchants of `gameconfig/shop/merchants.json`. They equal V1's four files byte for byte, so the hash does not move.
- **mythic-quests.** Read the `mythic_{characterId}_{number}` quests of the `hero` group in `gameconfig/quests/groups.json`. The 117 V1 characters are identical; the game adds `lostaBile` and `orksWeirdboy`, so the hash moves.
- `Data/unit-power.json`, `Data/mythic-quests.json`, `Data/shops/*` and `Data/raid-boss-*` are deleted, with their source-key lists. Twelve datamine files join `files.txt`.
- No rename, so no `SchemaVersion` bump.

## Capabilities

### Modified Capabilities

- `unit-power-dataset`: the source becomes the datamine power tables.
- `raid-bosses-dataset`: the source becomes the datamine guildBoss files; adds the unit-set merge rule.
- `game-shops-dataset`: the source becomes `merchants.json`.
- `mythic-quests-dataset`: the source becomes the `hero` quest group; coverage goes from 117 to 119.

## Impact

- Companion apps change: **none**. Shapes are unchanged; the two new mythic-quest characters are game data.
- `src/TacticusPlanner.GameCatalog`: `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, the four denormalizers and their models, deleted V1 files, new datamine copies.
- Tests: mythic-quest counts (117 → 119, 1755 → 1785 rewards); new reader tests; the manifest snapshot moves for `mythic-quests` only.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
