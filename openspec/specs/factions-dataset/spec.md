# factions-dataset Specification

## Purpose

Describes the served `factions` dataset: each game faction with its alliance, unlock date and collection flag, built
from the pinned datamine.

## Requirements

### Requirement: Factions are built from the pinned datamine file

The served `factions` dataset SHALL be built from the datamine copy `gameconfig/units/factions.json`, one record per
faction, ordered by unlock time, then id.

#### Scenario: The shipped factions

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 23 records, the first `Ultramarines` (Imperial, unlocked `2022-08-14T00:00:00Z`) and the last
  `TheLostAndTheDamned` (Chaos, a collection faction)

### Requirement: Each faction carries its alliance, unlock date and collection flag

Each served record SHALL carry `id`, `alliance` (the faction's single `traits` entry), `unlockDateUtc` (the game's
`unlockTimestamp` as `yyyy-MM-ddTHH:mm:ssZ`) and `collectionFaction` (absent = false).

#### Scenario: A collection faction

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** exactly `AdeptusAstartes` and `TheLostAndTheDamned` have `collectionFaction` true

### Requirement: Unmappable factions fail startup

The catalog loader SHALL fail startup, naming the dataset and the faction, when a faction's `traits` is not exactly one
of Imperial, Chaos or Xenos, or it has no positive `unlockTimestamp`.

#### Scenario: Two alliances

- **GIVEN** a faction with traits `[Imperial, Chaos]`
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `factions` and the faction

### Requirement: Unit factions resolve to served factions

Validation SHALL fail startup with an `InvalidFaction` error when a character, MoW or non-object NPC has a faction the
dataset does not serve, or an alliance different from its faction's.

#### Scenario: A missing faction

- **GIVEN** a factions dataset without `Orks`
- **WHEN** the catalog is validated
- **THEN** an `InvalidFaction` error names an Orks unit and its faction
