# mows-dataset Specification

## Purpose

Describes the served `mows` dataset's fields that clients depend on, starting with each Machine of War's mythic
ability, so the MoW unit page can show the ability unlocked at Mythic rarity.

## Requirements

### Requirement: Each MoW carries its mythic ability

Each served `mows` record SHALL carry, in addition to its existing fields, `mythicAbility`: an object with
exactly `name` (string, the ability id, the same id space as `primaryAbility.name` and
`secondaryAbility.name`). It SHALL be authored on the raw `mows[]` record in `units-{faction}.json` and served
unchanged. It is optional in the shape: a MoW whose mythic ability is unknown SHALL omit the field, not send
`null` or an empty name. Unlike `primaryAbility` and `secondaryAbility`, it SHALL carry no `recipes`: the
mythic ability levels with the MoW's blue stars, not with upgrade materials. All 11 current MoWs SHALL carry
it, ported from V1's `mow/data/new-mows-data2.json` (`mythicAbilities[0]`). The record SHALL carry no display
name or icon for it; the client resolves both from the id.

#### Scenario: Galatian's mythic ability

- **WHEN** the `mows` dataset is served
- **THEN** the `ultraDreadnought` record has `mythicAbility: { name: "WisdomOfTheAncients" }`

#### Scenario: Every MoW has one

- **WHEN** the `mows` dataset is served
- **THEN** all 11 records carry a `mythicAbility` with a non-empty `name`, and the 11 names are distinct

#### Scenario: The manifest hash changes

- **WHEN** the game catalog manifest is requested after this change
- **THEN** the `mows` entry's hash differs from the previous release, and the snapshot test is promoted

### Requirement: The mythic ability is validated at load

The catalog loader SHALL fail startup, naming the dataset, when a raw `mows[]` record has a `mythicAbility`
whose `name` is empty or equals that MoW's `primaryAbility.name` or `secondaryAbility.name`.

#### Scenario: Empty mythic ability name

- **GIVEN** a raw MoW with `mythicAbility: { name: "" }`
- **WHEN** the catalog loads
- **THEN** loading fails with a validation error naming the MoW
