# Tasks

Companion apps change: roadmap step 7 "Use `factions` and `damage-types`" (after all step 7 API PRs). Source plan:
`tacticus-planner-data-mines` `docs/roadmap.md` step 7.

## 1. Binding

- [x] 1.1 Add `gameconfig/units/factions.json` and `gameconfig/units/damageProfiles.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineFactions` and `ReadDatamineDamageTypes` and load them through `DatamineFiles.Open`
- [x] 1.3 Register `factions` and `damage-types` as served datasets, add their endpoints, bump `SchemaVersion` to 11

## 2. Validation

- [x] 2.1 Fail startup on a faction without one alliance or an unlock time, and on a damage type without a 0-100 piercing ratio
- [x] 2.2 Validate unit factions, alliances and dealt damage types against the new datasets

## 3. Verification

- [x] 3.1 Add `FactionDenormalizerTests` (shipped values, ordering, failures, cross-checks)
- [x] 3.2 Manifest snapshot: two new datasets, `schemaVersion` 11, no other hash moves
- [x] 3.3 Format, build, all test projects

## 4. Docs

- [x] 4.1 Update the `game-catalog-data` skill (both mirrors)
