## Purpose

Allows separate projects to carry separate in-flight targets while preventing duplicate Active/Paused goal types for one unit inside a project.

## ADDED Requirements

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

A friendly pre-check or database constraint conflict SHALL produce the same structured 400 response containing each conflicting project id/name, entity type/id, goal type, and existing goal id.

#### Scenario: Multi-project create partially conflicts

- **GIVEN** A is occupied and B is available
- **WHEN** one request targets A and B
- **THEN** the request is rejected atomically and identifies A and its existing goal

### Requirement: Database enforces concurrent slot occupancy

Persistence SHALL include a project-membership-level partial unique constraint that prevents two concurrent transactions from occupying the same project/unit/type slot.

#### Scenario: Racing creates target one slot

- **WHEN** two requests concurrently create matching in-flight goals in one project
- **THEN** at most one commits and the other returns the documented conflict
