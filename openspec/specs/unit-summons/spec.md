# unit-summons Specification

## Purpose

Describes the `summons` field of the served `characters` and `mows` datasets: whether a unit can bring a summon
onto the battlefield and whether the player controls it. Clients use it for "no summons" rules (an LRE "No Summons"
battle, the HRE summon-free mission) and to filter units.

## Requirements

### Requirement: Every character and MoW serves its summon type

Each served `characters` and `mows` record SHALL carry `summons`: `none` (no ability summons), `active` (only an
active ability summons, so the player can leave it unused) or `passive` (a passive ability can summon on its own,
so the player cannot prevent it). A unit with both an active and a passive summon SHALL be `passive`.

#### Scenario: A passive summoner

- **WHEN** the `characters` dataset is served
- **THEN** `blackPossession` (Archimatos, whose passive summons on a kill) has `summons: "passive"`

#### Scenario: An active summoner

- **WHEN** the `characters` dataset is served
- **THEN** `necroSpyder` (Aleph-Null, whose Scarab Hive is an active ability) has `summons: "active"`

#### Scenario: A MoW

- **WHEN** the `mows` dataset is served
- **THEN** `tyranBiovore` has `summons: "active"` and `blackForgefiend` has `summons: "none"`

### Requirement: The summon type is curated

The type SHALL come from `Data/curated/unit-summons.json`, an object from unit id to `active` or `passive`. An
unlisted unit SHALL be served `none`.

#### Scenario: An unlisted unit

- **GIVEN** `ultraCalgar` is not in `unit-summons.json`
- **WHEN** the `characters` dataset is served
- **THEN** its `summons` is `none`

### Requirement: The curation agrees with the datamine

Loading SHALL fail, naming `unit-summons` and the unit, when a curated id is not a `lineup.json` unit, a curated type
is not `active` or `passive`, a unit has an ability whose `gameconfig/units/abilities.json` entry has a `summonHp…`
variable (the summon's stat table) but no curated type, or a curated unit has no such ability. A datamine bump that
adds a summoner therefore fails until its type is curated.

#### Scenario: A new summoner in a datamine bump

- **GIVEN** a new character whose active ability has a `summonHp` variable and no `unit-summons` entry
- **WHEN** the catalog loads
- **THEN** loading fails naming the character

#### Scenario: The shipped curation

- **WHEN** the catalog loads at datamine `v1.44.88.3089`
- **THEN** 11 characters are `passive`, 15 characters and 6 MoWs are `active`, and every other unit is `none`
