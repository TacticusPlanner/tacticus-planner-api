## MODIFIED Requirements

### Requirement: Raid-boss unit sets are merged across the game's sets

`unitSets.json` holds the unit sets per set id (`unit_set_1`, `unit_set_2`). The served dataset has one unit-set map shared by every season, so the loader SHALL merge the sets in game order and SHALL fail startup, naming `raid-bosses` and the unit-set key, when two sets define the same key differently.

#### Scenario: Identical sets merge

- **WHEN** the datamine at `v1.43.157.2989` has `unit_set_1` and `unit_set_2` with the same 98 unit sets
- **THEN** they merge into one map of 98 unit sets and loading succeeds

#### Scenario: Diverging sets fail

- **GIVEN** `unit_set_2` gives `GuildBoss1Boss1TyranTervigonLeviathan` a different stat row than `unit_set_1`
- **WHEN** the catalog loads
- **THEN** loading fails naming `raid-bosses` and that unit-set key

## REMOVED Requirements

### Requirement: Raid-boss records carry no display text or icon

**Reason**: D1 — restates the catalog-wide ids-only rule.

**Migration**: `game-catalog-http-caching` — "Every served dataset is public and listed in the manifest".

### Requirement: Adding the raid-bosses dataset does not bump the schema version

**Reason**: L5 — one-off transition rule ("Introducing `raid-bosses` SHALL be purely additive") that only applied while the dataset was added.

**Migration**: The surviving `GameVersion` rule moves to `game-catalog-datamine-source` — "The catalog game version is the pinned datamine's build".
