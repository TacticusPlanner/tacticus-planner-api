# Tasks

Companion apps change: none (shapes unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add the twelve datamine files to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Load unit-power, raid-bosses, shops and mythic-quests from the datamine; add `ReadDatamineRaidBossUnitSets`, `ReadDatamineShops`, `ReadDatamineMythicQuests`
- [x] 1.3 Delete the V1 files and their source-key lists

## 2. Verification

- [x] 2.1 Compare old and new: unit-power, raid-bosses, shops identical; mythic-quests adds two characters
- [x] 2.2 Add reader tests; update the mythic-quest counts
- [x] 2.3 Promote the manifest snapshot (`mythic-quests` only)
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
