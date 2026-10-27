# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 7 adds catalog datasets for game data the apps still hardcode.
The apps keep daily energy (288) and the energy refill tiers (`entities/planning-setting`), Onslaught runs per day
(`ONSLAUGHT_RUNS_PER_DAY = 1.5` in `shop-supply.ts`), team sizes 3 and 5 (about six files) and the raid-boss trait
constants (`TRAIT_GLOBAL_VARIABLES` in `generate-raid-boss-i18n.mjs`, two of them wrong against the game). The game
ships all of them in `globalValues.json` and the energy shop ladder in `shop/staminaShopTiers.json`.

## What Changes

- **New dataset `global-values`.** A one-element list holding, under the game's names: energy (`maxStamina`,
  `staminaRegenerationTime`, `staminaRegenerationAmount`), Onslaught tokens (`waves*`), limited-battle attempts
  (`campaignLevel*`), team size (`minUnitsNeededForBattle`, `maxUnitsPossibleForBattle`), `terrifyingBonus`,
  `battleFatigueFleeChance`, `battleFatigueFleeChanceOverkill`, `traitVariables` (as shipped) and `staminaShopTiers`
  (each energy purchase's `productId`, `stamina`, `raidTickets` and `gems`, resolved through `shop/products.json`).
- **Served at** `/api/v1/game-catalog/global-values`, listed in the manifest. `SchemaVersion` 11 → 12. No existing
  dataset hash moves (`lres` reads the same `products.json`, now loaded once).
- **Validation.** A missing or non-positive stamina or team value, a max team size below the minimum, a fatigue
  chance outside 0-100, empty trait variables, or a shop tier that is not a gem-priced `stamina:N` (+ optional
  `raidTicket:N`) product fails startup.

## Capabilities

### New Capabilities

- `global-values-dataset`: the served game-wide constants, their datamine source and load-time checks.

### Modified Capabilities

None.

## Impact

- Companion apps change: roadmap step 7 apps PR "Use `global-values`" derives daily energy
  (86400 / `staminaRegenerationTime` × amount = 288), the refill tiers (plus the curated "+90 ad" tier the game config
  does not hold), Onslaught runs per day (86400 / `wavesStaminaRegenerationTime` = 1.5), team sizes and the raid-boss
  trait constants. It ships after the step 7 API PRs; today's apps skip the unknown key.
- `src/TacticusPlanner.GameCatalog`: `Models/GlobalValues.cs`, `Denormalization/GlobalValuesDenormalizer.cs`,
  `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, `Models/GameCatalogSnapshot.cs`,
  `Models/GameCatalogRelease.cs`; datamine copies `globalValues.json`, `shop/staminaShopTiers.json`.
- `src/TacticusPlanner.Api`: one served endpoint; OpenAPI artifact.
- Tests: new `GlobalValuesDenormalizerTests`; manifest snapshot gains one dataset and `schemaVersion` 12.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no infra change.
