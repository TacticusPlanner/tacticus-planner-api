## MODIFIED Requirements

### Requirement: Season timing uses explicit event occurrences only

The `guild-raid-season` catalog definition MAY be a `Fixed` recurrence, projected into the events calendar like any other `Fixed` definition (currently a 14-day interval, 13-day duration, anchored 2026-10-21T05:00:00Z). `endsAt` SHALL still be populated only from an explicitly authored `GuildRaidSeason` occurrence matching the active observation by `season` or `seasonConfigId`; projected placeholders carry no parameters and SHALL NOT be used for `endsAt`. The endpoint considers only the current game-catalog projection window and version. If no matching authored occurrence exists, `endsAt` SHALL be null while all other active status remains available.

#### Scenario: Explicit season end is available

- **WHEN** the current catalog version contains an active Guild Raid occurrence matching the observed season
- **THEN** its end timestamp is returned as `endsAt`

#### Scenario: Season end is not known

- **WHEN** no explicit matching occurrence exists, even though a projected Guild Raid placeholder covers the observation time
- **THEN** `endsAt` is null and the server does not derive a countdown from the projection
