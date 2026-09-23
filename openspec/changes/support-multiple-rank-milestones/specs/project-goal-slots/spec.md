## MODIFIED Requirements

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

### Requirement: Database enforces concurrent slot occupancy

Persistence SHALL include project-membership-level partial unique constraints that prevent two concurrent transactions from occupying the same non-Rank project/unit/type slot or the same Rank project/unit/normalized-end-target slot.

#### Scenario: Racing creates target one slot

- **WHEN** two requests concurrently create Ragnar Rank goals with the same normalized end target in one project
- **THEN** at most one commits and the other returns the documented structured conflict

#### Scenario: Racing distinct Rank targets succeed

- **WHEN** two requests concurrently create Ragnar Rank goals with distinct targets in one project
- **THEN** both can commit with distinct valid priorities

## ADDED Requirements

### Requirement: Rank conflicts name the occupied target

For a Rank slot conflict, the structured response SHALL include the existing goal id, project id/name, entity id/type, goal type, and normalized end target; a stale membership or status transition SHALL fail atomically across all affected projects.

#### Scenario: Multi-project membership conflicts in one project

- **GIVEN** a requested Rank goal belongs to A and B, and B already contains its normalized target
- **WHEN** the request is submitted
- **THEN** neither membership is changed and the response identifies B, the target, and B's existing goal
