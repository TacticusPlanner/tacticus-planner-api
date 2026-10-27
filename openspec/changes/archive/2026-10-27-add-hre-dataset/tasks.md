# Tasks

Companion apps change: roadmap step 7 "Use `hre`" (after all step 7 API PRs). Source plan:
`tacticus-planner-data-mines` `docs/roadmap.md` step 7.

## 1. Binding

- [x] 1.1 Add `leaderboards.json` and `liveEvents/heroReleaseEvent.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineHres` (event, scoring, milestones, mission chain, leaderboard and phase chests, chest shop, offer); load `chests.json` once for it and `lres`
- [x] 1.3 Register `hre` as a served dataset, add its endpoint, bump `SchemaVersion` to 14

## 2. Validation

- [x] 2.1 Fail startup on unmappable rewards, broken chains and missing sources
- [x] 2.2 Validate the hero, rarities, faction and mission task parameters (`InvalidHre`)

## 3. Verification

- [x] 3.1 Add `HreDenormalizerTests` (shipped values against the apps' wiki tables, a small event, failures, references)
- [x] 3.2 Manifest snapshot: one new dataset, `schemaVersion` 14, no other hash moves
- [x] 3.3 Format, build, all test projects

## 4. Docs

- [x] 4.1 Update the `game-catalog-data` skill (both mirrors)
