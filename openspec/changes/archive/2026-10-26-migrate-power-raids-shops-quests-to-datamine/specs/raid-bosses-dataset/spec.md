## MODIFIED Requirements

### Requirement: The catalog serves one consolidated `raid-bosses` dataset

The game catalog SHALL serve a dataset keyed `raid-bosses` built from the pinned datamine copies under `globalconfig/guildBoss/`: `guildBossSeasonConfigRotation.json`, `primarchs.json`, `modifiers.json`, `unitSets.json` and `guildBossSeasonDataConfigsGDTO.json`. The catalog SHALL keep no hand-kept raid-boss files. It SHALL be the only raid-boss dataset exposed publicly; the raw per-file sources (season rotation, unit sets, season configs, modifier definitions) SHALL NOT be served directly. The served dataset SHALL be self-contained: an encounter's boss/prime stats, weapons, abilities, traits, and scaled modifier definitions are inlined so the client never joins one served collection against another.

#### Scenario: Raid-bosses dataset is served without authentication

- **WHEN** a client requests the `raid-bosses` endpoint
- **THEN** the data is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: Raid-bosses dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `raid-bosses`

#### Scenario: Raw sources have no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving a raw raid-boss source file (season rotation, unit sets, season configs, or modifier definitions) directly

## ADDED Requirements

### Requirement: Raid-boss unit sets are merged across the game's sets

`unitSets.json` holds the unit sets per set id (`unit_set_1`, `unit_set_2`). The served dataset has one unit-set map shared by every season, so the loader SHALL merge the sets in game order and SHALL fail startup, naming `raid-bosses` and the unit-set key, when two sets define the same key differently.

#### Scenario: Identical sets merge

- **WHEN** the datamine at `v1.43.157.2989` has `unit_set_1` and `unit_set_2` with the same 98 unit sets
- **THEN** the served bosses and primes equal the ones the retired `raid-boss-*.json` files served, and the `raid-bosses` hash is unchanged

#### Scenario: Diverging sets fail

- **GIVEN** `unit_set_2` gives `GuildBoss1Boss1TyranTervigonLeviathan` a different stat row than `unit_set_1`
- **WHEN** the catalog loads
- **THEN** loading fails naming `raid-bosses` and that unit-set key
