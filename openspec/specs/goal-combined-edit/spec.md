# goal-combined-edit Specification

## Purpose
Lets an owner change several aspects of one goal (target, details, project memberships, priority) in a single request that either applies completely or changes nothing, so a multi-field edit cannot half-apply.

## Requirements

### Requirement: An owner can edit a goal through one atomic request

`PUT /me/goals/{goalId}` SHALL accept a request with these optional sections: `target` (`expectedRevision` plus the goal kind's end-target payload), `details` (`notes`, `farmingLocationIds`, `farmingStrategy`, `acquisitionSources`), `projectIds` (the complete replacement membership list), and `priority` (`position`, 1-based, and `expectedOrderRevision`). A section that is absent SHALL leave that aspect unchanged. A request with no section SHALL succeed as a no-op. On success the response SHALL be 200 with `{ goal, order }`: `goal` SHALL contain the updated goal detail (including its own `revision`); `order` SHALL contain `{ revision, goalIds }` when `priority` was supplied, and SHALL be null otherwise. The order uses the existing `PUT /me/goals/order` field names. Every applied section SHALL be committed together or none SHALL be, with a single revision increase. This is the only goal edit route.

#### Scenario: Several sections at once

- **GIVEN** an Active goal
- **WHEN** the owner submits a new target, new notes, and a new position in one request
- **THEN** all three are saved and the response carries `{ goal, order }` with the goal revision under `goal.revision` and the order revision under `order.revision`

#### Scenario: Absent sections are untouched

- **WHEN** the owner submits only `details.notes`
- **THEN** the target, project memberships and priority are unchanged

#### Scenario: Empty request

- **WHEN** the owner submits no section
- **THEN** the response is 200 with the unchanged goal under `goal`, `order: null`, and no revision change

#### Scenario: Unknown or foreign goal

- **WHEN** the goal does not exist or belongs to another account
- **THEN** the response is 404 and nothing changes

### Requirement: Each section is validated exactly as its own endpoint validates it

The target section SHALL be validated as a target edit (see `goal-target-editing`: only Active or Paused Rank, Ascension, Ability and Upgrade goals; the payload shape of the goal's own kind; the goal's stored baseline; an unchanged target is a no-op; `expectedRevision` is required with a target and a stale one is a 409 `goalRevisionStale` carrying the current goal). The details section SHALL be validated as goal details (notes length, farming strategy support per goal kind, farming-location and acquisition-source rules; a null `notes` or `farmingLocationIds` clears the field and a null `farmingStrategy` leaves it). The projects section SHALL require at least one owned project; it is not slot-checked. The priority section SHALL be valid only for an Active or Paused goal; `position` SHALL be between 1 and the number of in-flight goals; the goal takes that position and every goal between its old and new positions shifts one place toward the vacated one; a `position` equal to the current one is a no-op. A validation failure in any section SHALL be a 400 that names the offending section and changes nothing.

#### Scenario: Invalid target rejects the whole request

- **WHEN** the request carries valid notes and an invalid target
- **THEN** the response is 400 naming the target, and the notes are not saved

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

A stale `expectedRevision` SHALL return 409 `goalRevisionStale` with the current goal under `current`. A stale `expectedOrderRevision` SHALL return 409 `goalOrderStale` with the current order revision and goal ids under `current`. A slot conflict SHALL return 409 `slotOccupied` as defined in `project-goal-slots`. All three use the unified error shape (`api-error-contract`). In every 409 no section SHALL have been applied, including sections that would have been valid alone.

#### Scenario: Stale goal revision with valid other sections

- **WHEN** the goal changed elsewhere and the request carries a target with the old revision plus new notes
- **THEN** the response is 409 `goalRevisionStale`, and the notes are not saved

#### Scenario: Stale order revision

- **WHEN** the account's order changed since the client loaded it and the request carries `priority` plus a valid target change
- **THEN** the response is 409 `goalOrderStale` with the current order, and the target change is not saved

### Requirement: The edit is safe under concurrent goal, project and order changes

The edit SHALL run under the profile-level lock shared by every goal mutation, SHALL re-read the goal and its memberships under that lock, and SHALL write nothing to goal, membership or order state outside that transaction.

#### Scenario: Concurrent membership change

- **WHEN** another request adds the goal to a project while this edit waits for the lock
- **THEN** the edit validates against the reloaded memberships and both requests complete

#### Scenario: Concurrent edits do not deadlock

- **WHEN** two edits of goals sharing two projects run at the same time
- **THEN** both complete, one after the other, without a deadlock
