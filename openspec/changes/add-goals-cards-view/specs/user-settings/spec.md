## ADDED Requirements

### Requirement: User settings expose a Goals view preference

The user-settings resource SHALL include a `goalsView` value that is exactly one of `Cards` or `Table`. The value is a stable id; the server SHALL NOT send display names or icons for it. A profile that has never chosen a value, including one whose stored settings predate this field, SHALL read as `Cards`. A stored value that is not one of the two supported values SHALL also read as `Cards` and SHALL never be returned as stored.

#### Scenario: New profile reads the default

- **WHEN** an authenticated profile with no stored settings requests its user settings
- **THEN** the response has `goalsView` equal to `Cards`, alongside the existing `dailyEnergy`, `xpBookRarity` and `revision`

#### Scenario: Legacy stored settings read the default

- **WHEN** a profile's stored settings contain a `dailyEnergy` and an `xpBookRarity` but no Goals view
- **THEN** its response has the stored `dailyEnergy` and `xpBookRarity`, and `goalsView` equal to `Cards`

#### Scenario: An unsupported stored value reads the default

- **WHEN** a profile's stored settings hold a Goals view value that is neither `Cards` nor `Table`
- **THEN** its response has `goalsView` equal to `Cards`

### Requirement: Goals view preference is saved with user settings

A user-settings update SHALL require `goalsView` together with `dailyEnergy`, `xpBookRarity` and `revision`, and SHALL persist either supported value under the existing revision check. An update whose `goalsView` is missing, empty, or not one of the two supported values SHALL be rejected as a validation error and SHALL NOT change stored settings or the revision. Values are read case-insensitively and returned in PascalCase, like every enum (see `openapi-contract`).

#### Scenario: Saving the Table view

- **WHEN** the profile submits an update with `goalsView` equal to `Table`, a supported `dailyEnergy` and `xpBookRarity`, and the current `revision`
- **THEN** the response and later reads return `Table` and the revision advances by one

#### Scenario: Changing only the Goals view keeps the other settings

- **GIVEN** a profile whose stored settings are `dailyEnergy` 438 and `xpBookRarity` `Epic`
- **WHEN** it submits an update with `dailyEnergy` 438, `xpBookRarity` `Epic`, `goalsView` `Table` and the current `revision`
- **THEN** later reads return `dailyEnergy` 438, `xpBookRarity` `Epic` and `goalsView` `Table`

#### Scenario: Rejecting an unsupported or missing view

- **WHEN** an update carries `goalsView` equal to `Grid`, an empty value, or omits it
- **THEN** the request is rejected as a bad request and stored settings and revision are unchanged

#### Scenario: Stale revision still conflicts

- **WHEN** an update with a valid `goalsView` carries a revision that is not current
- **THEN** it is rejected as a conflict, nothing is stored, and the conflict response carries the current settings including their `goalsView`
