# project-count-limit Specification

## Purpose
The per-profile cap on non-archived projects, and how archiving a project frees a slot since projects cannot be deleted.

## Requirements

### Requirement: A profile holds at most 100 non-archived projects

A profile SHALL hold at most 100 projects outside Archived, of any type, the Default project included. Archived projects SHALL NOT count, so archiving a project frees a slot: there is no project delete.

`POST projects` at the cap SHALL be a 400 with `issueCode` `projectLimitReached` and SHALL create nothing. The count SHALL be taken under the profile lock, so concurrent creates cannot both take the last slot.

#### Scenario: Create at the cap

- **GIVEN** a profile with 100 non-archived projects
- **WHEN** it sends `POST projects`
- **THEN** the response is 400 `projectLimitReached` and no project is created

#### Scenario: Archiving frees a slot

- **GIVEN** a profile with 100 non-archived projects, one of which it then archives
- **WHEN** it sends `POST projects`
- **THEN** the project is created
