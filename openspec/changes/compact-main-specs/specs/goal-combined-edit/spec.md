## MODIFIED Requirements

### Requirement: Each section is validated exactly as its own endpoint validates it

The target section SHALL be validated as a target edit (see `goal-target-editing`: only Active or Paused Rank, Ascension, Ability and Upgrade goals; the payload shape of the goal's own kind; the goal's stored baseline; an unchanged target is a no-op; `expectedRevision` is required with a target). The details section SHALL be validated as goal details (notes length, farming strategy support per goal kind, farming-location and acquisition-source rules; a null `notes` or `farmingLocationIds` clears the field and a null `farmingStrategy` leaves it). The projects section SHALL require at least one owned project; it is not slot-checked. The priority section SHALL be valid only for an Active or Paused goal; `position` SHALL be between 1 and the number of in-flight goals; the goal takes that position and every goal between its old and new positions shifts one place toward the vacated one; a `position` equal to the current one is a no-op. A validation failure in any section SHALL be a 400 that names the offending section and changes nothing.

#### Scenario: Invalid target rejects the whole request

- **WHEN** the request carries valid notes and an invalid target
- **THEN** the response is 400 naming the target, and the notes are not saved

#### Scenario: Project slot check sees the new target

- **GIVEN** a Rank goal whose new target is already held by another in-flight goal of the same unit
- **WHEN** the request changes the goal's target
- **THEN** the response is 409 `slotOccupied` naming the existing goal, and nothing is saved

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
