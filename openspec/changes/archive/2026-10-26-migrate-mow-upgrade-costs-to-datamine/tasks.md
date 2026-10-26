# Tasks

Companion apps change: none (nothing served changes). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add `gameconfig/units/abilityUpgradeCostsMoW.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineMowUpgradeCosts` (key mapping, defaults, unmapped-key failure) and load it through `DatamineFiles.Open`
- [x] 1.3 Delete `Data/mow-upgrade-costs.json` and drop it from `GameCatalogDatasets.Required`

## 2. Verification

- [x] 2.1 Compare old and new ladders row by row: identical
- [x] 2.2 Add `MowUpgradeCostTests` (shipped samples, key mapping, defaults, missing and unknown keys)
- [x] 2.3 Manifest snapshot unchanged
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
