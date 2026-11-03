## MODIFIED Requirements

### Requirement: Projects store an optional event target

A project SHALL store an optional target `{ kind, eventId, run, season, date }`, where `kind` is one of `legendaryEvent`, `battlePass`, `guildRaidSeason`, `guildWarSeason` or `customDate`, and every other field is optional and nullable. `POST me/projects` and `PUT me/projects/{projectId}` SHALL accept `target` (object or null; absent on PUT means null), and every project response SHALL carry `target` (null when none) with all five fields present, the unused ones null.

The allowed fields SHALL depend on `kind`:

- `legendaryEvent`: `eventId` SHALL be a served `lres` id (required). `run` SHALL be 1, 2, 3 or null (null means "next run"). `season` and `date` SHALL be null.
- `battlePass`, `guildRaidSeason`, `guildWarSeason`: `season` SHALL be a positive integer (a pinned season) or null (rolling). `eventId`, `run` and `date` SHALL be null. The season number is not checked against the catalog.
- `customDate`: `date` SHALL be a calendar date `YYYY-MM-DD` (required, past dates allowed). `eventId`, `run` and `season` SHALL be null.

The system SHALL reject with 400, naming the field, an unknown `kind`, a field that does not belong to the kind, a missing required field, an out-of-range `run` or `season`, an invalid `date`, an `eventId` that is not a served `lres` id, and any target on the Default project. Resending a project's stored target unchanged SHALL pass even when the catalog no longer serves its event. Archiving or restoring a project SHALL keep its target. The server SHALL NOT resolve or return a deadline instant.

#### Scenario: Create with a target

- **WHEN** the caller posts `{ name "Uthar run 3", target { kind "legendaryEvent", eventId "votanUthar" } }`
- **THEN** the response is 200 and the project carries `target { kind "legendaryEvent", eventId "votanUthar", run null, season null, date null }`

#### Scenario: Legendary Event run

- **WHEN** the caller posts `{ name "Bile run 1", target { kind "legendaryEvent", eventId "lostaBile", run 1 } }`
- **THEN** the response is 200 and the project carries `run 1`

#### Scenario: Run out of range

- **WHEN** the caller posts a `legendaryEvent` target with `run` 4
- **THEN** the response is 400 naming `target.run`

#### Scenario: Pinned and rolling seasons

- **WHEN** the caller posts `target { kind "guildWarSeason", season 29 }`, and later updates the project to `target { kind "battlePass", season null }`
- **THEN** both calls return 200, and the project first carries `season 29` and then `kind "battlePass"`, `season null`

#### Scenario: Field that does not belong to the kind

- **WHEN** the caller posts `target { kind "battlePass", season 42, eventId "votanUthar" }`
- **THEN** the response is 400 naming `target.eventId`

#### Scenario: Custom date

- **WHEN** the caller posts `target { kind "customDate", date "2026-11-20" }`
- **THEN** the response is 200 and the project carries `date "2026-11-20"`

#### Scenario: Invalid or missing date

- **WHEN** the caller posts `target { kind "customDate", date "2026-02-30" }`, or `target { kind "customDate" }`
- **THEN** the response is 400 naming `target.date`

#### Scenario: Unknown event

- **WHEN** the caller posts a target with `eventId` "notAnEvent"
- **THEN** the response is 400 naming `target.eventId`

#### Scenario: Default project

- **WHEN** the caller updates the Default project with a target of any kind
- **THEN** the response is 400 naming `target`

#### Scenario: Clear the target

- **GIVEN** a project targeting `votanUthar`
- **WHEN** the caller updates it with `target` null
- **THEN** the project has no target

#### Scenario: Event left the catalog

- **GIVEN** a project targeting run 2 of an event the catalog no longer serves
- **WHEN** the caller archives it, resending that target unchanged
- **THEN** the response is 200 and the project keeps the target

#### Scenario: Existing client payload

- **GIVEN** a project stored before this change with `TargetKind` LegendaryEvent and `TargetEventId` "votanUthar"
- **WHEN** the caller lists projects
- **THEN** its target reads `{ kind "legendaryEvent", eventId "votanUthar", run null, season null, date null }`
