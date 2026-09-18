## Purpose

Allows separate projects to carry separate in-flight targets while preventing duplicate Active/Paused goal types for one unit inside a project.

## Requirements

### Requirement: In-flight uniqueness is scoped to project

For each project, at most one Active/Paused goal SHALL occupy `(entityType, entityId, goalType)`. Completed/Archived goals SHALL not occupy the slot. Different projects MAY contain different in-flight goals for the same tuple.

#### Scenario: Different projects accept different instances

- **GIVEN** A has an Active Ragnar Rank goal
- **WHEN** another Ragnar Rank goal is created only in B
- **THEN** creation succeeds

#### Scenario: Same project rejects duplicate

- **GIVEN** A has an Active or Paused Ragnar Rank goal
- **WHEN** another in-flight Ragnar Rank goal is created in or added to A
- **THEN** the operation is rejected

#### Scenario: Historical goal frees slot

- **GIVEN** A contains only Completed/Archived Ragnar Rank goals
- **WHEN** a new Ragnar Rank goal is created in A
- **THEN** creation succeeds

### Requirement: Every membership and lifecycle path enforces slots

The invariant SHALL apply atomically to single creation, combined creation, goal-side membership replacement, project-side membership replacement, and transitions into Active/Paused. Removing membership or transitioning to Completed/Archived SHALL free affected slots.

#### Scenario: Resume conflicts in one membership

- **GIVEN** a Paused/Completed goal belongs to A and B and another in-flight matching goal occupies B
- **WHEN** the goal is transitioned into an in-flight status
- **THEN** the entire transition is rejected and no membership changes occupancy

#### Scenario: Shared goal occupies each project once

- **WHEN** one canonical in-flight goal belongs to A and B
- **THEN** it occupies the matching slot once in each without conflicting with itself

### Requirement: Conflicts identify affected projects and goals

A friendly pre-check or database constraint conflict SHALL produce the same structured HTTP 409 response containing each conflicting project id/name, entity type/id, goal type, and existing goal id.

#### Scenario: Multi-project create partially conflicts

- **GIVEN** A is occupied and B is available
- **WHEN** one request targets A and B
- **THEN** the request is rejected atomically and identifies A and its existing goal

### Requirement: Database enforces concurrent slot occupancy

Persistence SHALL include a project-membership-level partial unique constraint that prevents two concurrent transactions from occupying the same project/unit/type slot.

#### Scenario: Racing creates target one slot

- **WHEN** two requests concurrently create matching in-flight goals in one project
- **THEN** at most one commits and the other returns the documented conflict

### Requirement: Concurrent mutations in one project serialize without failing

Goal mutations that target one project SHALL be serialized against each
other, and each SHALL observe the state committed by those that preceded it.
Serialization SHALL be achieved by waiting, not by aborting: when two or more
mutations that target distinct slots are submitted concurrently to one
project, every one of them SHALL commit. A mutation SHALL NOT fail merely
because another mutation for the same project committed while it was waiting.

Mutations covered are single goal creation, combined goal creation,
transitions into or out of an in-flight status, goal-side membership
replacement, and project-side membership replacement.

#### Scenario: Concurrent creates for distinct units all commit

- **GIVEN** an owned project containing no goals
- **WHEN** creation requests for Ragnar, Aun'shi, and Certus are submitted concurrently to that project
- **THEN** all three requests succeed
- **AND** the project's listed goal order contains all three unit blocks with contiguous priorities

#### Scenario: Concurrent creates for distinct goal types on one unit all commit

- **GIVEN** an owned project containing no goals
- **WHEN** an Ascension and a Rank creation request for the same unit are submitted concurrently to that project
- **THEN** both requests succeed
- **AND** both goals appear in that unit's block

#### Scenario: Racing same-slot creates produce a conflict, not a server error

- **GIVEN** an owned project containing no goals
- **WHEN** two creation requests for the same unit and the same goal type are submitted concurrently to that project
- **THEN** exactly one succeeds
- **AND** the other is rejected with the documented structured slot-conflict response identifying the project and the existing goal
- **AND** neither request produces an unhandled server error

#### Scenario: Ordering stays contiguous under concurrency

- **GIVEN** an owned project containing goals for two units
- **WHEN** a create and a unit-order change are submitted concurrently to that project
- **THEN** both succeed
- **AND** the resulting in-flight priorities form a contiguous sequence starting at 1 with no duplicates and no gaps
