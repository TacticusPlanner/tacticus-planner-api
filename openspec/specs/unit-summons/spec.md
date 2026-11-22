# unit-summons Specification

## Purpose

Describes the `summons` field of the served `characters` and `mows` datasets: whether a unit can bring a summon
onto the battlefield and whether the player can keep it from doing so. Clients use it for "no summons" rules (an LRE
"No Summons" battle, the HRE summon-free mission) and to filter units.

## Requirements

### Requirement: Every character and MoW serves its summon type

Each served `characters` and `mows` record SHALL carry `summons`: `none` (no ability summons), `optional` (the
player decides whether it summons: an active ability left unused, or a passive the player can switch off) or
`forced` (a passive ability summons on its own, so the player cannot prevent it). A unit with both kinds SHALL be
`forced`.

#### Scenario: A forced summoner

- **WHEN** the `characters` dataset is served
- **THEN** `blackPossession` (Archimatos, whose passive summons on a kill) has `summons: "forced"`

#### Scenario: An optional summoner

- **WHEN** the `characters` dataset is served
- **THEN** `necroSpyder` (Aleph-Null, whose Scarab Hive is an active ability) and `admecMarshall` (Tan Gi'da, whose
  passive summons only while his active is switched on) have `summons: "optional"`

#### Scenario: A MoW

- **WHEN** the `mows` dataset is served
- **THEN** `tyranBiovore` has `summons: "optional"` and `blackForgefiend` has `summons: "none"`

### Requirement: The summon type is curated

The type SHALL come from `Data/curated/unit-summons.json`, an object from unit id to `optional` or `forced`. An
unlisted unit SHALL be served `none`.

#### Scenario: An unlisted unit

- **GIVEN** `ultraCalgar` is not in `unit-summons.json`
- **WHEN** the `characters` dataset is served
- **THEN** its `summons` is `none`

### Requirement: The curation agrees with the datamine

Loading SHALL fail, naming `unit-summons` and the unit, when a curated id is not a `lineup.json` unit, a curated type
is not `optional` or `forced`, a unit has an ability whose `gameconfig/units/abilities.json` entry has a
`summonHp…` variable (the summon's stat table) but no curated type, or a curated unit has no such ability. A datamine
bump that adds a summoner therefore fails until its type is curated.

#### Scenario: A new summoner in a datamine bump

- **GIVEN** a new character whose active ability has a `summonHp` variable and no `unit-summons` entry
- **WHEN** the catalog loads
- **THEN** loading fails naming the character

#### Scenario: The shipped curation

- **WHEN** the catalog loads at datamine `v1.44.88.3089`
- **THEN** 10 characters are `forced`, 16 characters and 6 MoWs are `optional`, and every other unit is `none`
