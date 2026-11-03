## MODIFIED Requirements

### Requirement: The Default project cannot be archived or removed

The Default project SHALL NOT be archivable, and no operation SHALL delete it. An attempt to archive it SHALL be rejected with a conflict identified as `defaultProjectCannotBeArchived`; an attempt to delete it SHALL be rejected with a conflict identified as `defaultProjectCannotBeDeleted`, carrying the project as it stands. Any custom project, archived or not, MAY be permanently deleted. The caller SHALL choose what happens to every goal whose only membership was the deleted project: by default it SHALL be filed into the Default project in the same transaction, keeping its status, target and global priority; with `deleteGoals=true` it SHALL be permanently deleted in the same transaction, exactly as a single goal delete (it leaves the global order, which compacts, and drops out of every dependent's dependencies). Goals with another membership SHALL never be deleted and keep that membership.

#### Scenario: Archiving the Default project is rejected

- **WHEN** the caller sets the Default project's status to Archived
- **THEN** the request is rejected with `defaultProjectCannotBeArchived` and the project keeps its status

#### Scenario: Deleting the Default project is rejected

- **WHEN** the caller deletes the Default project
- **THEN** the request is rejected with `defaultProjectCannotBeDeleted` and the project still exists

#### Scenario: Any custom project can be archived

- **GIVEN** a custom project
- **WHEN** the caller archives it
- **THEN** it is archived

#### Scenario: Deleting a custom project keeps its goals

- **GIVEN** a custom project with one goal that belongs only to it and one that also belongs to the Default project
- **WHEN** the caller deletes the project
- **THEN** the project no longer exists, both goals belong to the Default project, and neither goal's global priority changed

#### Scenario: Deleting a custom project together with its goals

- **GIVEN** a custom project with one goal that belongs only to it and one that also belongs to the Default project
- **WHEN** the caller deletes the project with `deleteGoals=true`
- **THEN** the project and the first goal no longer exist, the second goal remains in the Default project, and the global order is compacted
