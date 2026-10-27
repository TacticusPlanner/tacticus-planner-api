# mows-dataset Specification

## Purpose

Describes the served `mows` dataset's fields that clients depend on, starting with each Machine of War's mythic
ability, so the MoW unit page can show the ability unlocked at Mythic rarity.

## Requirements

### Requirement: Each MoW carries its mythic ability

Each served `mows` record whose MoW has a mythic ability SHALL carry `mythicAbility`: an object with
exactly `name` (string, the ability id, the same id space as `primaryAbility.name` and
`secondaryAbility.name`). It SHALL be read from the first entry of the MoW's `mythicAbilities` in the datamine
`gameconfig/units/lineup.json`. It is optional in the shape: a MoW without a mythic ability SHALL omit the field,
not send `null` or an empty name. Unlike `primaryAbility` and `secondaryAbility`, it SHALL carry no `recipes`:
the mythic ability levels with the MoW's blue stars, not with upgrade materials. All 11 current MoWs SHALL carry
it. The record SHALL carry no display name or icon for it; the client resolves both from the id.

#### Scenario: Galatian's mythic ability

- **WHEN** the `mows` dataset is served
- **THEN** the `ultraDreadnought` record has `mythicAbility: { name: "WisdomOfTheAncients" }`

#### Scenario: Every MoW has one

- **WHEN** the `mows` dataset is served
- **THEN** all 11 records carry a `mythicAbility` with a non-empty `name`, and the 11 names are distinct

### Requirement: The mythic ability is validated at load

The catalog loader SHALL fail startup, naming the dataset, when a raw `mows[]` record has a `mythicAbility`
whose `name` is empty or equals that MoW's `primaryAbility.name` or `secondaryAbility.name`.

#### Scenario: Empty mythic ability name

- **GIVEN** a raw MoW with `mythicAbility: { name: "" }`
- **WHEN** the catalog loads
- **THEN** loading fails with a validation error naming the MoW

### Requirement: MoWs are read from the pinned datamine

A `lineup.json` unit whose `traits` contain `MachineOfWar` SHALL be served as a MoW, grouped by lowercased `FactionId` and ordered by id, with `name`, faction and alliance from `name`, `FactionId` and `GrandAllianceId`.

#### Scenario: The shipped MoWs

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it serves 11 MoWs

### Requirement: MoW abilities and recipes come from the ability table

A MoW's two `activeAbilities` SHALL be its `primaryAbility` and `secondaryAbility`, each with `recipes` from that ability's `upgrades` in `gameconfig/units/abilities.json`. A MoW without exactly two active abilities, or whose ability has no recipes, SHALL fail startup naming the MoW.

#### Scenario: One active ability

- **GIVEN** a MoW with a single active ability
- **WHEN** the catalog loads
- **THEN** loading fails naming the MoW
