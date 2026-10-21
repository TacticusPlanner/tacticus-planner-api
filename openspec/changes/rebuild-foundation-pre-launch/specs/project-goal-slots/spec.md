## MODIFIED Requirements

### Requirement: In-flight uniqueness is scoped to project

For each profile, at most one Active/Paused non-Rank goal SHALL occupy `(entityType, entityId, goalType)`. For Rank, at most one Active/Paused goal SHALL occupy `(entityType, entityId, Rank, normalizedEndTarget)`; distinct Rank end targets MAY coexist. Completed/Archived goals SHALL not occupy slots. Project membership SHALL NOT affect slot occupancy: one in-flight goal MAY belong to any number of projects, and two projects SHALL NOT carry separate in-flight instances of the same tuple/target.

#### Scenario: Different projects accept different instances

- **GIVEN** an Active Ragnar Rank goal targeting Gold1 belongs to project A
- **WHEN** the owner wants Ragnar Gold1 in project B
- **THEN** the existing goal is added to B; creating a second Ragnar Rank Gold1 goal is rejected and identifies the existing goal

#### Scenario: Same project rejects duplicate

- **GIVEN** an Active or Paused Ragnar Rank goal targeting Gold1 in any project
- **WHEN** another in-flight Ragnar Rank goal with the same normalized Gold1 target is created, in any project set
- **THEN** the operation is rejected and identifies the existing goal

#### Scenario: Historical goal frees slot

- **GIVEN** the profile has only Completed/Archived Ragnar Rank goals targeting Gold1
- **WHEN** a new Ragnar Rank Gold1 goal is created
- **THEN** creation succeeds

#### Scenario: Same project permits different Rank targets

- **GIVEN** an Active Ragnar Rank goal targeting Silver3
- **WHEN** another targeting Gold1 is created
- **THEN** both are accepted

### Requirement: Every membership and lifecycle path enforces slots

The invariant SHALL apply atomically to single creation, combined creation, target edits, and transitions into Active/Paused (single or bulk). Transitioning to Completed/Archived SHALL free the slot. Membership changes (goal-side or project-side) SHALL NOT be slot-checked, because membership cannot create a duplicate.

#### Scenario: Resume conflicts in one membership

- **GIVEN** a Completed goal A followed by creation of another Active/Paused goal B occupying the same slot
- **WHEN** the caller attempts to transition A to Paused
- **THEN** the transition is rejected with 409 `slotOccupied` identifying B, and A remains Completed

#### Scenario: Shared goal occupies each project once

- **GIVEN** an in-flight goal in project A
- **WHEN** it is added to project B or removed from A
- **THEN** the operation is not slot-checked and succeeds (subject to the last-project rule)

### Requirement: Conflicts identify affected projects and goals

A friendly pre-check or database constraint conflict SHALL produce the same 409 in the unified error shape with `issueCode` `slotOccupied`, naming the entity type/id, goal type, normalized end target (Rank only) and the existing goal id, and carrying the existing goal under `current`.

#### Scenario: Multi-project create partially conflicts

- **GIVEN** the profile already has an in-flight Ragnar Rank Gold1 goal
- **WHEN** one combined request creates Ragnar Unlock, Ascension and Rank Gold1
- **THEN** the request is rejected atomically with `slotOccupied` identifying the existing Rank goal, and no goal is created

### Requirement: Database enforces concurrent slot occupancy

Persistence SHALL include profile-level partial unique constraints on the goal itself that prevent two concurrent transactions from occupying the same non-Rank profile/unit/type slot or the same Rank profile/unit/normalized-end-target slot.

#### Scenario: Racing creates target one slot

- **WHEN** two requests concurrently create Ragnar Rank goals with the same normalized end target
- **THEN** at most one commits and the other returns the documented `slotOccupied` conflict

#### Scenario: Racing distinct Rank targets succeed

- **WHEN** two requests concurrently create Ragnar Rank goals with distinct targets
- **THEN** both can commit with distinct valid priorities

### Requirement: Concurrent mutations in one project serialize without failing

Goal mutations of one profile SHALL be serialized against each other by waiting on one profile-level lock, and each SHALL observe the state committed by those that preceded it. When two or more slot-scoped mutations target distinct slots and are submitted concurrently, every one of them SHALL commit; a slot-scoped mutation SHALL NOT fail merely because another mutation for the same profile committed while it was waiting. A goal-order change is not slot-scoped: when it races a mutation that changes the in-flight set, whichever commits second observes the earlier change, and an order change whose submitted set no longer matches SHALL be rejected with the documented stale-set response. Covered mutations: single and combined creation, status transitions, target edits, membership replacement (goal- or project-side), and goal-order changes.

#### Scenario: Concurrent creates for distinct units all commit

- **WHEN** creation requests for Ragnar, Aun'shi, and Certus are submitted concurrently
- **THEN** all three succeed and the in-flight order contains all three with distinct, valid priorities

#### Scenario: Concurrent creates for distinct goal types on one unit all commit

- **WHEN** an Ascension and a Rank creation request for the same unit are submitted concurrently
- **THEN** both succeed and both goals appear in the in-flight order

#### Scenario: Racing same-slot creates produce a conflict, not a server error

- **WHEN** two creation requests for the same unit and goal type are submitted concurrently
- **THEN** exactly one succeeds, the other is rejected with `slotOccupied` identifying the existing goal, and neither produces an unhandled server error

#### Scenario: Ordering stays contiguous under concurrency

- **WHEN** a create and a goal-order change (submitted for the pre-create goal set) race
- **THEN** the create succeeds, the order change either succeeds or is rejected with the stale-set response, and in both outcomes the in-flight priorities form a contiguous sequence from 1 with no gaps or duplicates

### Requirement: Rank conflicts name the occupied target

For a Rank slot conflict, the `slotOccupied` response SHALL include the existing goal id, entity id/type, goal type and normalized end target; a conflicting target edit or status transition SHALL fail atomically.

#### Scenario: Multi-project membership conflicts in one project

- **GIVEN** an in-flight Ragnar Rank goal targeting Silver3 and another targeting Gold1
- **WHEN** the Silver3 goal's target is edited to Gold1
- **THEN** nothing changes and the response identifies the Gold1 goal and the target
