## MODIFIED Requirements

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

#### Scenario: The manifest hash changes

- **WHEN** the game catalog manifest is requested after the mythic ability was added
- **THEN** the `mows` entry's hash differs from the release before it, and the snapshot test is promoted

### Requirement: MoWs are read from the pinned datamine

A `lineup.json` unit whose `traits` contain `MachineOfWar` SHALL be served as a MoW, grouped by lowercased `FactionId` and ordered by id, with `name`, faction and alliance from `name`, `FactionId` and `GrandAllianceId`.

#### Scenario: Same MoWs as before

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** the 11 MoWs equal the ones the retired `units-*.json` files served, and the `mows` hash is unchanged

#### Scenario: The shipped MoWs

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it serves 11 MoWs
