# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 6 migrates the catalog onto the pinned datamine, in bigger batches as the maintainer asked. This change moves the unit tables: characters and MoWs (V1 `new-character-data.json`, `new-rank-up-data.json`, `new-mow-data.json`, `new-mows-data2.json`, ported to `units-*.json`) and NPCs (V1 `new-npc-data.json`, ported to `npcs-*.json`).

## What Changes

- **characters, mows.** Read from `gameconfig/units/lineup.json` plus `gameconfig/units/abilities.json`. A unit with the `MachineOfWar` trait is a MoW: its two active abilities, with their `upgrades` recipes, are primary and secondary, and `mythicAbilities[0]` is the mythic one. Everything else is a character: stats, weapons, traits without `Hero`, abilities, damage profiles (distinct, sorted), item slots, and rank-up rows from Stone1. The output is identical, so no hash moves.
- **npcs.** Read from `gameconfig/units/npc.json` plus `abilities.json`, using the same weapon, trait and damage-profile rules. Faction-less loot objects form `npcs-objects` (`Objects` / `Neutral`). An absent weapon, movement or stat number is 0. The `npcs` hash moves, and every difference is game data:
  - 55 NPCs get the game's current stat rows (V1's ladders were stale, e.g. `blackBossAbaddonLEG` gains a Diamond2 row).
  - 14 ability lists take game order, where V1 had sorted them.
  - `votanNpc2Steeljack` movement goes from 2 to 3.
  - Aeldari, Dark Angels and Votann NPCs are served in id order, like every other faction.
- `GameCatalogFactionUnits.Name` / `GameCatalogFactionNpcs.Name` carry the faction id. The faction display name was never served, and languages stay out of the API.
- `Data/units/*` and `Data/npcs/*` are deleted, along with the `UnitFactions` / `NpcFactions` key lists. The three datamine files join `files.txt`.
- No rename, so no `SchemaVersion` bump.

## Capabilities

### New Capabilities

- `characters-dataset`: the characters' datamine source, field mapping and load checks.

### Modified Capabilities

- `npcs-dataset`: the source becomes the datamine; the projection table is restated against the game's fields.
- `mows-dataset`: the mythic ability comes from `lineup.json`; adds the datamine source requirement.

## Impact

- Companion apps change: **none**. Shapes are unchanged, and the npcs changes are game data.
- `src/TacticusPlanner.GameCatalog`:
  - `Denormalization/UnitDatamineReader.cs` (new)
  - `Models/DatamineUnits.cs` (new)
  - `GameCatalogLoader.cs`, `Models/GameCatalogDatasets.cs`
  - deleted V1 files
  - new datamine copies (`lineup.json`, `npc.json`, `abilities.json`, about 3.9 MB)
- Tests:
  - new `UnitDatamineReaderTests`
  - `LreDenormalizerTests` follows Helbrecht's third stat row
  - `DatamineFilesTests` uses another unlisted path
  - the manifest snapshot moves for `npcs` only
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
