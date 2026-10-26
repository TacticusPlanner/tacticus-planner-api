# Tasks

Companion apps change: none (served shape unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6 and `docs/catalog-audit.md`.

## 1. Binding

- [x] 1.1 Add `gameconfig/units/abilityUpgradeCosts.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineCharacterAbilityCosts` (row position → `lvl`, token key → badge rarity) and load it through `DatamineFiles.Open`
- [x] 1.3 Delete `Data/character-ability-costs.json` and drop it from `GameCatalogDatasets.Required`; delete the V1 rarity bands
- [x] 1.4 Validate rows without exactly one token key and unknown rarities (`InvalidCost`)

## 2. Verification

- [x] 2.1 Compare old and new served ladders: only levels 52 and 53 gold differ
- [x] 2.2 Rewrite `CharacterAbilityCostTests` (shipped samples, V1-band agreement, token-key reader, invalid rows)
- [x] 2.3 Promote the manifest snapshot: only the `character-ability-costs` hash moves
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)

Follow-up after merge: note `character-ability-costs` as migrated in `tacticus-planner-data-mines` `docs/roadmap.md`.
