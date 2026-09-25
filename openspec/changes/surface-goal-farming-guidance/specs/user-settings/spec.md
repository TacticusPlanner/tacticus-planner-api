## Purpose

Defines the account-wide user settings a profile persists and reads through the API, starting with the XP-book rarity preference the client uses to express Level-goal XP needs as a book equivalent.

## ADDED Requirements

### Requirement: User settings expose an XP-book rarity

The user-settings resource SHALL include an `xpBookRarity` value that is exactly one of `Common`, `Uncommon`, `Rare`, `Epic`, `Legendary`, or `Mythic`. The value is a stable id; the server SHALL NOT send display names or icons for it. A profile that has never chosen a value, including one whose stored settings predate this field, SHALL read as `Legendary`.

#### Scenario: New profile reads the default

- **WHEN** an authenticated profile with no stored settings requests its user settings
- **THEN** the response has `xpBookRarity` equal to `Legendary` alongside the existing `dailyEnergy` and `revision`

#### Scenario: Legacy stored settings read the default

- **WHEN** a profile's stored settings contain a `dailyEnergy` but no XP-book rarity
- **THEN** its response has that `dailyEnergy` and `xpBookRarity` equal to `Legendary`

### Requirement: XP-book rarity is saved with user settings

A user-settings update SHALL require `xpBookRarity` together with `dailyEnergy` and `revision`, and SHALL persist all supported values under the existing revision check. An update whose `xpBookRarity` is missing, empty, or not one of the six supported values SHALL be rejected as a validation error and SHALL NOT change stored settings or the revision. Values are case-sensitive exactly as listed.

#### Scenario: Saving a supported rarity

- **WHEN** the profile submits an update with `xpBookRarity` equal to `Epic`, a supported `dailyEnergy`, and the current `revision`
- **THEN** the response and later reads return `Epic` and the revision advances by one

#### Scenario: Rejecting an unsupported rarity

- **WHEN** an update carries `xpBookRarity` equal to `Godly`, or omits it
- **THEN** the request is rejected as a bad request and stored settings and revision are unchanged

#### Scenario: Stale revision still conflicts

- **WHEN** an update with a valid `xpBookRarity` carries a revision that is not current
- **THEN** it is rejected as a conflict and nothing is stored
