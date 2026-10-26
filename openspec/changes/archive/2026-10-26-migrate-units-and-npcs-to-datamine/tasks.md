# Tasks

Companion apps change: none (shapes unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add `abilities.json`, `lineup.json` and `npc.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineUnits` / `ReadDatamineNpcs` and load them through `DatamineFiles.Open`
- [x] 1.3 Delete `Data/units/*` and `Data/npcs/*` and the `UnitFactions` / `NpcFactions` key lists

## 2. Verification

- [x] 2.1 Compare old and new: characters and mows identical; npcs differences listed in the proposal
- [x] 2.2 Add `UnitDatamineReaderTests`; update the LRE ladder test and the unlisted-path test
- [x] 2.3 Promote the manifest snapshot (`npcs` only)
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
