# Tasks

Companion apps change: none (shapes unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add `battles/onslaught.json`, `liveEvents/homeScreenEvent.json`, `liveEvents/linearHeroEvent.json` and `loot/tieredProgressRewards.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineOnslaughtRewards`, `ReadDatamineHseScoring` (porting `import-hse-scoring.py`) and `ReadDatamineQuestEvents`
- [x] 1.3 Drop the hand-entered quest battle power (the game config has none)
- [x] 1.4 Delete the V1 files and the import script

## 2. Verification

- [x] 2.1 Compare old and new: differences limited to Bronze-1 badges, Defeat Waves default ladders, two new quests
- [x] 2.2 Add `EventDatamineReaderTests`; update the quest-events coverage test
- [x] 2.3 Promote the manifest snapshot (`event-definitions`, `onslaught-rewards`, `quest-events`)
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
