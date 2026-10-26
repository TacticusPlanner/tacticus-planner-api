# Tasks

Companion apps change: none (shapes unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add `heroProgressionSteps.json`, `heroConversion.json`, `upgrades.json` and `languages/en.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineAscensionCosts`, `ReadDatamineUnlockShardCosts` and `ReadDatamineUpgrades`, and load them through `DatamineFiles.Open`
- [x] 1.3 Delete `Data/ascension-costs.json`, `Data/unlock-shard-costs.json` and `Data/upgrades/*`, and drop them from `GameCatalogDatasets.Required`
- [x] 1.4 Add `ValidateAscensionCosts` (progression order, non-negative costs, orbs with a rarity)

## 2. Verification

- [x] 2.1 Dump the served views on main and on the branch: ascension identical, unlock drops Mythic, upgrades add `upgArmL008` only
- [x] 2.2 Add `ProgressionCostTests` and upgrade reader tests; update the loader unlock test
- [x] 2.3 Promote the manifest snapshot (`unlock-shard-costs`, `upgrades`)
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
