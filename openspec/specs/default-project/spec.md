# default-project Specification

## Purpose
Guarantees each profile has exactly one Default project that acts as the goal filing fallback and can never be archived or removed, independent of any other project selection.

## Requirements

### Requirement: Every profile has exactly one Default project

Each profile SHALL have exactly one project of type Default, provisioned when the account is created and, for any profile that lacks one, created by migration or on first need. The system SHALL NOT allow two Default projects for one profile, including under concurrent requests. Custom projects SHALL be unaffected by this rule.

#### Scenario: New account gets a Default project

- **WHEN** an account is provisioned
- **THEN** its profile has exactly one Default project and no other project

#### Scenario: Existing profile without a Default project

- **GIVEN** a profile that has no Default project
- **WHEN** the migration has run or the profile's projects are first needed
- **THEN** exactly one Default project exists for that profile, with its existing goals and memberships unchanged

#### Scenario: Concurrent creation cannot produce two Defaults

- **WHEN** two requests concurrently ensure the Default project for the same profile
- **THEN** exactly one Default project exists afterwards and neither request fails

### Requirement: The Default project cannot be archived or removed

The Default project SHALL NOT be archivable, and no operation SHALL delete it. An attempt to archive it SHALL be rejected with a conflict identified as `defaultProjectCannotBeArchived`. No other project SHALL be blocked from archiving because of any selection or plan standing, since the system SHALL NOT record any project as the profile's active plan.

#### Scenario: Archiving the Default project is rejected

- **WHEN** the caller sets the Default project's status to Archived
- **THEN** the request is rejected with `defaultProjectCannotBeArchived` and the project keeps its status

#### Scenario: Any custom project can be archived

- **GIVEN** a custom project
- **WHEN** the caller archives it
- **THEN** it is archived

### Requirement: The Default project is editable and the filing fallback

The Default project's name, description and color SHALL remain editable. Goals created or imported without a named target project SHALL be filed into the Default project. Emptying the Default project SHALL be allowed provided every goal still belongs to at least one other project; removing a goal's only remaining membership SHALL be rejected, so a client that wants to move it out of a project adds the destination membership first.

#### Scenario: Default project is renamed

- **WHEN** the caller renames the Default project
- **THEN** the new name is stored and it remains the Default project

#### Scenario: Goal created without a target project

- **WHEN** the caller creates a goal without naming any target project
- **THEN** the goal belongs to the Default project

#### Scenario: Only remaining membership cannot be removed

- **GIVEN** a goal belonging only to a custom project
- **WHEN** the caller removes that membership without adding another
- **THEN** the request is rejected and the goal keeps its membership

### Requirement: No active-project selection is exposed

Project reads SHALL NOT expose an active-plan flag, and the API SHALL NOT offer an operation that marks a project as the profile's active plan. Project responses SHALL identify the Default project by its type only.

#### Scenario: Project summaries carry no active-plan flag

- **WHEN** the caller lists projects
- **THEN** no project summary contains an active-plan indicator

#### Scenario: The activate operation is gone

- **WHEN** a caller requests the former activate operation for a project
- **THEN** the API responds as for an unknown route and no project changes
