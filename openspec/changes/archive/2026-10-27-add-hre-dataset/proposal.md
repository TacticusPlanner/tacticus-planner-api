# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 7 adds catalog datasets for game data the apps still hardcode.
The apps' Hero Release Event planner ships the 21 missions (`hre-missions/model/hre-missions.ts`, from a community
sheet) and the milestone, leaderboard bracket and chest tables (`hre-reward-tables.ts`, from the wiki). The game ships
all of it: `liveEvents/heroReleaseEvent.json` (hero, scoring, milestones, Oh So Close offer), the `hre{N}` quest
chains in `quests/groups.json`, `heroReleaseLeaderboard*` in `leaderboards.json`, and the event chests in
`loot/chests.json` and `shop/products.json`.

## What Changes

- **New dataset `hre`.** One record per Hero Release Event, ordered by id: `heroId`, `baseRarity`, `maxRarity`,
  `featuredFaction`, `gameModes`, `scorePoints`, `milestones` (`scorePoints`, `currency`), `missions` (`index`, `id`,
  `bonus`, `currency`, `rewards`, `tasks` of `name`, `target`, `parameters`), `leaderboard` (`id`, `dailyRewards`,
  `phases` of `endOffsetDays` and per-bracket `tiers` with chest id, currency and shards), `chestShop` (`chestId`,
  `cost`, `minShards`, `maxShards`) and `ohSoCloseOffer`.
- **Served at** `/api/v1/game-catalog/hre`, listed in the manifest. `SchemaVersion` 13 → 14. No existing dataset hash
  moves (`chests.json` is now loaded once for `hre` and `lres`).
- **Validation.** An unmappable reward, a broken mission chain, a missing quest group, leaderboard or chest, or a
  non-currency chest price fails startup. The hero and mission task heroes must be served characters, the featured
  faction and task factions served factions, task alliances known and task damage types served (`InvalidHre`).

## Capabilities

### New Capabilities

- `hre-dataset`: the served Hero Release Events, their datamine sources and load-time checks.

### Modified Capabilities

None.

## Impact

- Companion apps change: roadmap step 7 apps PR "Use `hre`" replaces `HRE_MISSIONS` (mapping the game's task names to
  the planner's objective kinds), `HRE_MILESTONES` (cumulated by the client), `HRE_BRACKETS`, `HRE_CHESTS`,
  `HRE_MISSION_CURRENCY` and, with `progression-rules` `hreShards`, `HRE_RARITY_STEPS`. Values with no game source
  stay curated (14 daily payouts, the Oh So Close 25-shard cap). It ships after the step 7 API PRs; today's apps skip
  the unknown key.
- `src/TacticusPlanner.GameCatalog`: `Models/Hre.cs`, `Denormalization/HreDenormalizer.cs`, `Validation/HreValidation.cs`,
  `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, `Models/GameCatalogSnapshot.cs`,
  `Models/GameCatalogRelease.cs`; datamine copies `leaderboards.json`, `liveEvents/heroReleaseEvent.json`.
- `src/TacticusPlanner.Api`: one served endpoint; OpenAPI artifact.
- Tests: new `HreDenormalizerTests`; manifest snapshot gains one dataset and `schemaVersion` 14.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no infra change.
