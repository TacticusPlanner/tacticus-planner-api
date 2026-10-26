# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 6 migrates the existing catalog onto the pinned datamine. The maintainer asked for several datasets per change to move faster, so this change takes the last two cost tables and the upgrade materials together: `ascension-costs` (V1 port of `charsProgression` plus the orb calculator), `unlock-shard-costs` (V1 `charsUnlockShards`) and `upgrades` (V1 `recipeData`, split into eleven per-rarity files).

## What Changes

- **ascension-costs.** Read from `gameconfig/units/heroProgressionSteps.json`. `rarity` plus `stars` (0 to 14, mapped to `None` … `MythicWings`) give the progression. `costs.shards`, `costs.mythicShards` and `costs.heroAscensionOrb{Rarity}` give shards, mythic shards, orbs and orb rarity. The first step's game cost (40 shards) is the unlock, so it is served as free, as before. The result is identical, so no hash moves. New validation: the steps must be exactly `ProgressionRules.ProgressionOrder`, with no negative cost and no orbs without a rarity.
- **unlock-shard-costs.** Read from `gameconfig/units/heroConversion.json` (rarity → shards), in rarity order. Common to Legendary match. The game has no Mythic entry, and no character starts at Mythic, so V1's Mythic 1400 record is dropped. The shape is unchanged, but the hash moves. An unknown rarity key fails startup.
- **upgrades.** Read from `gameconfig/upgrades.json`. `label` and `material` are the game's untranslated `name`; localized names are left to the apps.
  - `statType` maps `hp` → `Health`, `dmg` → `Damage`, `fixedArmor` → `Armour`.
  - `crafting` gives `craftable` and the recipe.
  - The upgrades are still grouped into the same `upgrades-{rarity}[-crafted]` source keys, ordered by id, so the served order is unchanged.
  - The game adds `upgArmL008` (Transdimensional Sanctum, Legendary Armour), and the game's `name` drops two accents (`Mjød` → `Mjod`, `Tännhauser's Bones` → `Tannhauser's Bones`), so the hash moves. The other 557 records are identical.
  - An empty name or an unknown `statType` fails startup.
- The datamine copies of the four new files join `files.txt`. `Data/ascension-costs.json`, `Data/unlock-shard-costs.json` and `Data/upgrades/*` are deleted and leave `GameCatalogDatasets.Required`.
- No rename, so no `SchemaVersion` bump.

## Capabilities

### New Capabilities

- `ascension-costs-dataset`: the served ascension ladder, its datamine source and its checks.
- `unlock-shard-costs-dataset`: the served unlock costs, their datamine source and the dropped Mythic record.
- `upgrades-dataset`: the served upgrade materials' datamine source, naming, stat mapping and grouping.

### Modified Capabilities

None.

## Impact

- Companion apps change: **none**. The apps look up unlock cost by a character's starting rarity, which is never Mythic, and the new upgrade is just another record.
- `src/TacticusPlanner.GameCatalog`:
  - `Denormalization/ProgressionCostDenormalizer.cs` and `UpgradeDenormalizer.cs` (readers)
  - `Validation/AscensionCostValidation.cs`
  - `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`, `Models/ProgressionCosts.cs`, `Models/Upgrades.cs`
  - deleted V1 files
  - new datamine copies
- Tests: new `ProgressionCostTests`; upgrade reader tests; the loader unlock test drops Mythic; the manifest snapshot moves for `unlock-shard-costs` and `upgrades`.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
