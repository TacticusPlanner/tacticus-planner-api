## ADDED Requirements

### Requirement: User settings control automatic roster snapshots

The user-settings resource SHALL include a boolean `autoRosterSnapshots`. A profile that has never set it, including one whose stored settings predate this field, SHALL read as `true`. An update MAY omit `autoRosterSnapshots`, which leaves it unchanged. When it is provided, it SHALL be persisted under the existing revision check. Turning it off SHALL NOT delete existing automatic snapshots.

#### Scenario: Default on

- **WHEN** a profile with no stored settings reads its user settings
- **THEN** `autoRosterSnapshots` is `true`

#### Scenario: Older client omits the field

- **GIVEN** `autoRosterSnapshots` is `false`
- **WHEN** an update with `dailyEnergy`, `xpBookRarity` and `revision` but no `autoRosterSnapshots` is saved
- **THEN** `autoRosterSnapshots` stays `false`
