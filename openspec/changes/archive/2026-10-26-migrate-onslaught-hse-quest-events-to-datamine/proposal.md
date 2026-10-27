# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 6 migrates the catalog onto the pinned datamine, in batches. This change moves the event-side datasets: `onslaught-rewards`, the Home Screen Event scoring served as `event-definitions` `config.hse`, and `quest-events`.

## What Changes

- **onslaught-rewards.** Read from `gameconfig/battles/onslaught.json`: shard ranges from `honorYourHeroesRewards` (the top step of each rarity), badges from the `tierWaves` round rewards. The hash moves for one value: `Bronze-1` has 1 Common badge in the game, where V1's hand-kept file had 2. The other 20 tiers are identical.
- **HSE scoring.** Read from `liveEvents/homeScreenEvent.json`, `loot/tieredProgressRewards.json` and the `EnvStatBoost:` abilities, with the mapping of the retired `import-hse-scoring.py` ported to C#. Every definition is identical except `hse-defeat-waves` `default`: the game replaced its High and Mid ladders and has no Low ladder any more, so that tier is dropped. The `event-definitions` hash moves.
- **quest-events.** Read from `liveEvents/linearHeroEvent.json`. The three V1 quests are identical; the game adds `darkaSternguard` and `thousSekhetar`, so the hash moves. Battle power is not in the game config, so the served `power` field is dropped.
- **quest-crates stays hand-kept.** The game gives each Quest Event its own crate track (seasonal currency in Sekhetar's, Epic orbs instead of Legendary in crate 14 of the last three), so the shared track cannot come from the datamine without a shape change.
- `onslaught-rewards.json`, `events/home-screen-event-scoring.json`, `quests/quest-events.json` and `import-hse-scoring.py` are deleted. Four datamine files join `files.txt`.
- No rename, so no `SchemaVersion` bump.

## Capabilities

### New Capabilities

- `onslaught-rewards-dataset`: the datamine source and derivation of onslaught rewards.

### Modified Capabilities

- `home-screen-event-scoring`: the scoring is read from the datamine at load instead of an imported file.
- `quest-events-dataset`: the source becomes `linearHeroEvent.json`, and the `power` field is dropped; coverage goes from 3 to 5 quests.

## Impact

- Companion apps change: **none**. Shapes are unchanged; the moves are game data.
- `src/TacticusPlanner.GameCatalog`: `Denormalization/EventDatamineReader.cs` and `Models/DatamineEvents.cs` (new), `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, deleted V1 files, new datamine copies.
- Tests: new `EventDatamineReaderTests`; the quest-events coverage test; the manifest snapshot moves for `event-definitions`, `onslaught-rewards` and `quest-events`.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
