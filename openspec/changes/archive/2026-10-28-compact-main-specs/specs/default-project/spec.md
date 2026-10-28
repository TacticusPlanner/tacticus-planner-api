## MODIFIED Requirements

### Requirement: The Default project cannot be archived or removed

The Default project SHALL NOT be archivable, and no operation SHALL delete it. An attempt to archive it SHALL be rejected with a conflict identified as `defaultProjectCannotBeArchived`.

#### Scenario: Archiving the Default project is rejected

- **WHEN** the caller sets the Default project's status to Archived
- **THEN** the request is rejected with `defaultProjectCannotBeArchived` and the project keeps its status

#### Scenario: Any custom project can be archived

- **GIVEN** a custom project
- **WHEN** the caller archives it
- **THEN** it is archived

### Requirement: Every profile has exactly one Default project

Each profile SHALL have exactly one project of type Default, provisioned when the account is created and, for any profile that lacks one, created on first need. The system SHALL NOT allow two Default projects for one profile, including under concurrent requests. Custom projects SHALL be unaffected by this rule.

#### Scenario: New account gets a Default project

- **WHEN** an account is provisioned
- **THEN** its profile has exactly one Default project and no other project

#### Scenario: Existing profile without a Default project

- **GIVEN** a profile that has no Default project
- **WHEN** the profile's projects are first needed
- **THEN** exactly one Default project exists for that profile, with its existing goals and memberships unchanged

#### Scenario: Concurrent creation cannot produce two Defaults

- **WHEN** two requests concurrently ensure the Default project for the same profile
- **THEN** exactly one Default project exists afterwards and neither request fails
