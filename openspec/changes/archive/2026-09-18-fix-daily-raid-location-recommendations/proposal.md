## Why

Two related player reports (via the in-app feedback widget) show Daily Raids picking the wrong farm location for a needed material:

1. For a material whose only real farm location is an event campaign's Extremis-tier node, the recommendation points there even when the player hasn't reached Extremis at all — because event-campaign battle-attempt data collapses Standard/Extremis into one ambiguous key, and the apps-side recommender treats "no data" as "available." This half needs **no catalog changes**: the account already syncs a `campaign-events-progress` chunk with a per-tier completed-battle high-water mark that the apps-side recommender simply doesn't consult yet. It's called out here only so both reports land under one paired change; the fix is apps-only.
2. When two farm locations tie on farming efficiency (`energyCost / dropRate`), V1 breaks the tie toward the location that pays more gold per raid (`orderBy(['energyPerItem', 'expectedGold'], ['asc', 'desc'])`); the ported apps-side selector stops after the efficiency filter and never applies that tie-break. It can't: the served game-catalog dataset's farm-location records omit gold entirely (`GameCatalogDenormalizer.BuildRewardLocations` explicitly drops any reward whose id is `gold`), so there's no gold figure client-side to break the tie with. Confirmed against raw campaign data: `FoCE13` and `SHME19` both cost 10 energy and share the same guaranteed-plus-`upgradeCommon_elite` reward for `upgDmgC010` ("Auspex"), differing only in guaranteed gold (109-165 vs. 123-180) — an exact tie the apps recommender currently resolves arbitrarily by array order instead of by value.

## What Changes

- Each served farm location gains an `expectedGold` figure: the average (`(min + max) / 2`) of its battle's guaranteed gold reward, so the apps-side selector can break energy-efficiency ties the way V1 does.
- No wire-contract removal; this is an additive field on the existing farm-location shape.
- **No player-data or migration changes** — `campaign-events-progress` already carries the per-tier unlock signal the companion apps change needs for the Extremis-visibility half of this fix.

## Capabilities

### Modified Capabilities
- `player-data-sync`: the farm-location requirement ("The game catalog SHALL represent rewards for the same resource and battle as one farm location...") gains an `expectedGold` field per location, computed from that battle's guaranteed gold reward.

## Impact

- `src/TacticusPlanner.GameCatalog/Denormalization/GameCatalogDenormalizer.cs` (`BuildRewardLocations`/`ResolveLocations`): stop discarding the `gold` reward; index each battle's guaranteed gold range and attach `expectedGold` to every `GameCatalogFarmLocation` built from that battle.
- `src/TacticusPlanner.GameCatalog/Models/GameCatalogDatasets.cs` (or wherever `GameCatalogFarmLocation` is declared): add the `expectedGold` field.
- Served upgrade/character-shard dataset schema and generated OpenAPI artifact: additive field, regenerate on build.
- Companion apps change: `tacticus-planner-apps` (same change name) — consumes `expectedGold` for the tie-break, and separately consumes the already-synced `campaign-events-progress` chunk to stop recommending unreached Extremis-tier nodes.
