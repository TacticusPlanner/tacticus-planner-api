# Tasks

Companion apps change: roadmap step 7 "XP and levels" and "Caps and stars" from `progression-rules` (after all step 7
API PRs). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 7.

## 1. Binding

- [x] 1.1 Add `consumables.json`, `units/upgradeSlots.json` and `units/xpLevels.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineProgressionRules` (XP curve, books, slot rows, hero and MoW ladders)
- [x] 1.3 Register `progression-rules` as a served dataset, add its endpoint, bump `SchemaVersion` to 13

## 2. Verification

- [x] 2.1 Add `ProgressionRulesDenormalizerTests` (shipped values, ordering, misshapen tables)
- [x] 2.2 Move `DatamineFilesTests`' never-listed path off `xpLevels.json`
- [x] 2.3 Manifest snapshot: one new dataset, `schemaVersion` 13, no other hash moves
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
