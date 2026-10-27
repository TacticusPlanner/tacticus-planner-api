# Design

## Context

At `v1.43.157.2989`, compared field by field against the V1-ported files:

- The six unit-power tables and the `lineup.json` power fields (`powerMultiplier`, `upgrades`, `upgradesStatIncrease`) are equal to `unit-power.json`.
- `unitSets.json` holds `unit_set_1` and `unit_set_2`, which are identical and hold the same 98 unit sets as the twelve `raid-boss-{n}-*.json` files. The only differences are `visualId` (not bound) and two loot objects whose missing `Movement` V1 wrote as 0. Rotation, primarchs and modifiers are equal; the season configs add `unitSet`, `seasonEndRewards` and `spawnPointsSet`, which are not served.
- `merchants.json` `guild`, `guildWars`, `elderShop` and `crusadeShop` equal the four `shops-*.json` files. Its `default` template has null fields, so only the four merchants are bound.
- `groups.json` `hero` holds 5 `mythic_` quests for each of 119 characters. 117 equal `mythic-quests.json`; `lostaBile` and `orksWeirdboy` are new.

## Decisions

### D1. Batch four datasets, keep every shape

No field is renamed, so the unit-power, raid-bosses and shops hashes prove the migration by not moving. The mythic-quests move is two new characters.

### D2. Bind straight into the existing raw records

The datamine field names match the existing raw records (case-insensitively), so the loader deserializes each file into them, and the denormalizers and validation are unchanged. The readers only pick and merge: the four merchants, the `mythic_` quests, the unit sets.

### D3. One unit-set map, guarded

The served dataset has one unit-set map shared by every season. The two game sets are identical today, so they are merged, and a key the sets define differently fails startup instead of serving one season's units for another.

### D4. Mythic quests by name

A mythic quest is identified by its name, `mythic_{characterId}_{number}`, not by its rewards. The character id and number come from the name, and a `mythic_` name that does not fit fails startup.
