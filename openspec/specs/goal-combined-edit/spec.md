# goal-combined-edit Specification

## Purpose
Lets an owner change several aspects of one goal (target, details, project memberships, priority) in a single request that either applies completely or changes nothing, so a multi-field edit cannot half-apply.

## Requirements

### Requirement: An owner can edit a goal through one atomic request

`PUT /me/goals/{goalId}/edit` SHALL accept a request with these optional sections: `target` (the same target payload as `PUT /me/goals/{goalId}/target`), `details` (`notes`, `farmingLocationIds`, `farmingStrategy`, `acquisitionSources`, with the same field semantics as `PUT /me/goals/{goalId}`), `projectIds` (the same replacement list as `PUT /me/goals/{goalId}/projects`), and `priority` (`position`, a 1-based position, and `expectedOrderRevision`). A section that is absent SHALL leave that aspect unchanged. A request with no section SHALL succeed as a no-op. On success the response SHALL be 200 with the updated goal detail, plus the new order (`revision` and `goalIds`) when `priority` was supplied. Every applied section SHALL be committed together or none of them SHALL be, and the goal SHALL show a single revision increase for the request.

#### Scenario: Several sections at once

- **GIVEN** an Active goal
- **WHEN** the owner submits a new target, new notes, and a new position in one request
- **THEN** all three are saved and the response carries the updated goal and the new order

#### Scenario: Absent sections are untouched

- **WHEN** the owner submits only `details.notes`
- **THEN** the target, project memberships and priority are unchanged

#### Scenario: Empty request

- **WHEN** the owner submits no section
- **THEN** the response is 200 with the unchanged goal and no revision change

#### Scenario: Unknown or foreign goal

- **WHEN** the goal does not exist or belongs to another account
- **THEN** the response is 404 and nothing changes

### Requirement: Each section is validated exactly as its own endpoint validates it

The target section SHALL be validated and applied as `PUT /me/goals/{goalId}/target` does (only Active or Paused Rank, Ascension, Ability and Upgrade goals; the payload shape of the goal's own kind; the goal's stored baseline; an unchanged target is a no-op; `expectedRevision` is required with a target and a stale one is a 409 `goalRevisionStale` carrying the current goal). The details section SHALL be validated as `PUT /me/goals/{goalId}` does (notes length, farming strategy support per goal kind, farming-location and acquisition-source rules; a null `notes` or `farmingLocationIds` clears the field and a null `farmingStrategy` leaves it). The projects section SHALL be validated as `PUT /me/goals/{goalId}/projects` does (at least one project, all owned, no project slot conflict, evaluated against the target this same request sets). The priority section SHALL be valid only for an Active or Paused goal; `position` SHALL be between 1 and the number of in-flight goals; the goal takes that position and every goal between its old and new positions shifts one place toward the vacated one; a `position` equal to the current one is a no-op. A validation failure in any section SHALL be a 400 that names the offending section and changes nothing.

#### Scenario: Invalid target rejects the whole request

- **WHEN** the request carries valid notes and an invalid target
- **THEN** the response is 400 naming the target, and the notes are not saved

#### Scenario: Project slot check sees the new target

- **GIVEN** a Rank goal whose new target is already held by another goal of the same unit in project P
- **WHEN** the request changes the goal's target and adds project P
- **THEN** the response is 409 with the project slot conflict and nothing is saved

#### Scenario: Position out of range

- **GIVEN** five in-flight goals
- **WHEN** the request asks for position 6
- **THEN** the response is 400 and nothing changes

#### Scenario: Priority on a goal without a position

- **WHEN** the request carries `priority` for a Completed goal
- **THEN** the response is 400 and nothing changes

#### Scenario: Move by position

- **GIVEN** in-flight goals A, B, C, D, E in that order
- **WHEN** the request moves E to position 3
- **THEN** the order becomes A, B, E, C, D, and moving C to position 5 from the original order gives A, B, D, E, C

### Requirement: Conflicts leave the goal unchanged and carry the current state

A stale `expectedRevision` SHALL return 409 `goalRevisionStale` with the current goal (the existing `GoalRevisionConflictResponse`). A stale or mismatched `expectedOrderRevision` SHALL return 409 with the existing order conflict body (issue code, message, current revision and goal ids). A project slot conflict SHALL return the existing project-slot conflict body. In every 409 no section SHALL have been applied, including sections that would have been valid alone.

#### Scenario: Stale goal revision with valid other sections

- **WHEN** the goal changed elsewhere and the request carries a target with the old revision plus new notes
- **THEN** the response is 409 `goalRevisionStale`, and the notes are not saved

#### Scenario: Stale order revision

- **WHEN** the account's order changed since the client loaded it and the request carries `priority` plus a valid target change
- **THEN** the response is 409 with the current order, and the target change is not saved

### Requirement: The edit is safe under concurrent goal, project and order changes

The edit SHALL hold the same project locks as the existing target, projects and order mutations for every project the goal belongs to before the edit and every project it belongs to after, taken in the existing deterministic order, and SHALL re-read the goal and its memberships under those locks, restarting with the widened lock set when membership drifted. Nothing SHALL be written to project or order state outside that transaction.

#### Scenario: Concurrent membership change

- **WHEN** another request adds the goal to a project while this edit waits for its locks
- **THEN** the edit restarts with the widened lock set and validates against the reloaded memberships

#### Scenario: Concurrent edits do not deadlock

- **WHEN** two edits of goals sharing two projects run at the same time
- **THEN** both complete, one after the other, without a deadlock

### Requirement: Existing endpoints keep their behavior

`PUT /me/goals/{goalId}/target`, `PUT /me/goals/{goalId}`, `PUT /me/goals/{goalId}/projects`, and `PUT /me/goals/order` SHALL keep their request shapes, responses, and error bodies.

#### Scenario: Existing endpoint regression

- **WHEN** an existing client calls any of the four endpoints
- **THEN** its request and response are unchanged from before this change
