# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 6 migrates the existing catalog onto the pinned datamine, one dataset per change, cost/progression tables first. `character-ability-costs` is the simplest of them: its hand-kept `Data/character-ability-costs.json` is a V1 port of the game's `units.abilityUpgradeCosts`, now available as the datamine slice `gameconfig/units/abilityUpgradeCosts.json`.

## What Changes

- **Source.** The ladder is read from `DatamineFiles.Open("gameconfig/units/abilityUpgradeCosts.json")`: one row per level, `gold` plus exactly one `abilityToken{Rarity}` count. Row N (1-based) is the cost from level N to N + 1, as V1's `lvl` was. `Data/character-ability-costs.json` is deleted and leaves `GameCatalogDatasets.Required`; the datamine file joins `files.txt`.
- **Badge rarity from the game.** `badges.rarity` comes from the token key (`abilityTokenEpic` → `Epic`) instead of V1's hard-coded level bands. At `v1.43.157.2989` the two agree on all 59 rows; the bands are deleted.
- **Served shape unchanged.** Same fields, same 59 records (levels 2 to 60). No `SchemaVersion` bump, no apps change.
- **Game-data fix.** V1's table had gold 127,500 at both `lvl` 50 and 51. The game steps by 2,500: the served levels 52 and 53 change from 127,500 / 130,000 to 130,000 / 132,500. Every other value is unchanged. This is the only reason the `character-ability-costs` manifest hash moves; no other hash moves.
- **Validation.** A row that does not have exactly one `abilityToken*` key besides `gold`, or names an unknown rarity, fails startup (`InvalidCost`).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `character-ability-costs-dataset`: source is the datamine file; badge rarity comes from the token key; validation covers the token key.

## Impact

- Companion apps change: **none** (shape unchanged). Clients refetch the dataset once for the new hash; goal costs for abilities raised to 52 and 53 grow by 2,500 gold each.
- `src/TacticusPlanner.GameCatalog`: `Denormalization/CharacterAbilityCostDenormalizer.cs`, `Validation/CharacterAbilityCostValidation.cs`, `Models/Mows.cs` (`GameCatalogCharacterAbilityCost.Badges` is now an amount by rarity), `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`; deleted `Data/character-ability-costs.json`; new datamine copy `gameconfig/units/abilityUpgradeCosts.json`.
- Tests: `CharacterAbilityCostTests` rewritten for the datamine reader; manifest snapshot promoted (`character-ability-costs` hash only).
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
