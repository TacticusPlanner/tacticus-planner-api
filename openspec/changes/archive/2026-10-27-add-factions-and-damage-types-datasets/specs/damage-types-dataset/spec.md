## ADDED Requirements

### Requirement: Damage types are built from the pinned datamine file

The served `damage-types` dataset SHALL be built from the datamine copy `gameconfig/units/damageProfiles.json`, one
record per damage type, ordered by id.

#### Scenario: The shipped damage types

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 22 records, the first `Acid` with piercing ratio 40 and no traits

### Requirement: Each damage type carries its piercing ratio and traits

Each served record SHALL carry `id`, `piercingRatio` (the game's `PiercingRatio`, a percentage) and `traits` (the
game's `Traits`, absent = empty).

#### Scenario: Flame

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** `Flame` has piercing ratio 25 and traits `[OnFire]`

### Requirement: Unmappable damage types fail startup

The catalog loader SHALL fail startup, naming the dataset and the damage type, when a damage type has no
`PiercingRatio` or one outside 0-100.

#### Scenario: No piercing ratio

- **GIVEN** a damage type `Bolter` with no `PiercingRatio`
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `damage-types` and `Bolter`

### Requirement: Dealt damage types resolve to served damage types

Validation SHALL fail startup with one `InvalidDamageType` error per damage type that a character or NPC deals (melee,
ranged, active or passive ability) and the dataset does not serve.

#### Scenario: A missing damage type

- **GIVEN** a damage-types dataset without `Bolter`
- **WHEN** the catalog is validated
- **THEN** one `InvalidDamageType` error names `Bolter`
