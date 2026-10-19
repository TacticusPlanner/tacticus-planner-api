## ADDED Requirements

### Requirement: Projects store an optional event target

A project SHALL store an optional target of `kind` `legendaryEvent` and an `eventId`. `POST me/projects` and `PUT me/projects/{projectId}` SHALL accept `target` (object or null; absent on PUT means null) and every project response SHALL carry `target` (null when none). The system SHALL reject with 400, naming the field, an unknown `kind`, an `eventId` that is not a served `lres` id, and any target on the Default project. Resending a project's stored target SHALL pass even when the catalog no longer serves its event. Archiving or restoring a project SHALL keep its target.

#### Scenario: Create with a target

- **WHEN** the caller posts `{ name "Uthar run 3", target { kind "legendaryEvent", eventId "votanUthar" } }`
- **THEN** the response is 200 and the project carries that target

#### Scenario: Unknown event

- **WHEN** the caller posts a target with `eventId` "notAnEvent"
- **THEN** the response is 400 naming `target.eventId`

#### Scenario: Default project

- **WHEN** the caller updates the Default project with a target
- **THEN** the response is 400 naming `target`

#### Scenario: Clear the target

- **GIVEN** a project targeting `votanUthar`
- **WHEN** the caller updates it with `target` null
- **THEN** the project has no target

#### Scenario: Event left the catalog

- **GIVEN** a project targeting an event the catalog no longer serves
- **WHEN** the caller archives it, resending that target
- **THEN** the response is 200 and the project keeps the target
