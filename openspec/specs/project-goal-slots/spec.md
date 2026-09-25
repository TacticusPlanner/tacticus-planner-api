## Purpose

Allows separate projects to carry separate in-flight targets while preventing duplicate Active/Paused goal types for one unit inside a project.

## Requirements

### Requirement: In-flight uniqueness is scoped to project

For each project, at most one Active/Paused non-Rank goal SHALL occupy `(entityType, entityId, goalType)`. For Rank, at most one Active/Paused goal SHALL occupy `(entityType, entityId, Rank, normalizedEndTarget)`; distinct Rank end targets MAY coexist. Completed/Archived goals SHALL not occupy slots. Different projects MAY carry different in-flight instances for the same tuple/target.

#### Scenario: Different projects accept different instances

- **GIVEN** A has an Active Ragnar Rank goal
- **WHEN** another Ragnar Rank goal is created only in B
- **THEN** creation succeeds

#### Scenario: Same project rejects duplicate

- **GIVEN** A has an Active or Paused Ragnar Rank goal targeting Gold1
- **WHEN** another in-flight Ragnar Rank goal with the same normalized Gold1 target is created in or added to A
- **THEN** the operation is rejected and identifies the existing goal

#### Scenario: Historical goal frees slot

- **GIVEN** A contains only Completed/Archived Ragnar Rank goals targeting Gold1
- **WHEN** a new Ragnar Rank Gold1 goal is created in A
- **THEN** creation succeeds

#### Scenario: Same project permits different Rank targets

- **GIVEN** A has an active Ragnar Rank goal targeting Silver3
- **WHEN** another targeting Gold1 is created in A
- **THEN** both memberships are accepted

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

Persistence SHALL include project-membership-level partial unique constraints that prevent two concurrent transactions from occupying the same non-Rank project/unit/type slot or the same Rank project/unit/normalized-end-target slot.

#### Scenario: Racing creates target one slot

- **WHEN** two requests concurrently create Ragnar Rank goals with the same normalized end target in one project
- **THEN** at most one commits and the other returns the documented structured conflict

#### Scenario: Racing distinct Rank targets succeed

- **WHEN** two requests concurrently create Ragnar Rank goals with distinct targets in one project
- **THEN** both can commit with distinct valid priorities

### Requirement: Concurrent mutations in one project serialize without failing

Goal mutations that target one project SHALL be serialized against each
other, and each SHALL observe the state committed by those that preceded it.
Serialization SHALL be achieved by waiting, not by aborting: when two or more
slot-scoped mutations (below) target distinct slots and are submitted
concurrently to one project, every one of them SHALL commit. A slot-scoped
mutation SHALL NOT fail merely because another mutation for the same project
committed while it was waiting.

A goal-order change is not slot-scoped — it validates against the project's
complete in-flight goal set, not a single slot — so it is exempt from the
"every one commits" guarantee above: when a goal-order change races a
mutation that changes which goals are in-flight (a create, or a transition
into or out of an in-flight status), whichever commits second observes a
membership the earlier one already changed, and a goal-order change whose
submitted set no longer matches SHALL be rejected with the documented
stale-set response, not committed and not an unhandled server error. This is
the existing "stale set is rejected atomically" contract, not a new failure
mode — concurrency only decides which of the two racing mutations, if
either, is the one that ends up stale.

Mutations covered are single goal creation, combined goal creation,
transitions into or out of an in-flight status, goal-side membership
replacement, and project-side membership replacement (all slot-scoped), plus
goal-order changes (not slot-scoped, see above).

#### Scenario: Concurrent creates for distinct units all commit

- **GIVEN** an owned project containing no goals
- **WHEN** creation requests for Ragnar, Aun'shi, and Certus are submitted concurrently to that project
- **THEN** all three requests succeed
- **AND** the project's listed goal order contains all three goals, each with a distinct, valid priority

#### Scenario: Concurrent creates for distinct goal types on one unit all commit

- **GIVEN** an owned project containing no goals
- **WHEN** an Ascension and a Rank creation request for the same unit are submitted concurrently to that project
- **THEN** both requests succeed
- **AND** both goals appear in the project's in-flight goal order

#### Scenario: Racing same-slot creates produce a conflict, not a server error

- **GIVEN** an owned project containing no goals
- **WHEN** two creation requests for the same unit and the same goal type are submitted concurrently to that project
- **THEN** exactly one succeeds
- **AND** the other is rejected with the documented structured slot-conflict response identifying the project and the existing goal
- **AND** neither request produces an unhandled server error

#### Scenario: Ordering stays contiguous under concurrency

- **GIVEN** an owned project containing goals for two units
- **WHEN** a create and a goal-order change (submitted for the pre-create goal set) race concurrently to that project
- **THEN** the create always succeeds
- **AND** the goal-order change either succeeds (if it wins the lock and commits before the create) or is rejected with the documented stale-set response (if the create commits first) — never an unhandled server error
- **AND** in both outcomes, the resulting in-flight priorities form a contiguous sequence starting at 1 with no duplicates and no gaps

### Requirement: Rank conflicts name the occupied target

For a Rank slot conflict, the structured response SHALL include the existing goal id, project id/name, entity id/type, goal type, and normalized end target; a stale membership or status transition SHALL fail atomically across all affected projects.

#### Scenario: Multi-project membership conflicts in one project

- **GIVEN** a requested Rank goal belongs to A and B, and B already contains its normalized target
- **WHEN** the request is submitted
- **THEN** neither membership is changed and the response identifies B, the target, and B's existing goal
