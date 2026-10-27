## RENAMED Requirements

- FROM: `### Requirement: In-flight uniqueness is scoped to project`
- TO: `### Requirement: In-flight uniqueness is scoped to the profile`

- FROM: `### Requirement: Concurrent mutations in one project serialize without failing`
- TO: `### Requirement: Concurrent goal mutations of one profile serialize without failing`

## MODIFIED Requirements

### Requirement: The profile lock serializes slot checks

Every goal write SHALL take the profile lock before checking slots, so two concurrent transactions cannot both occupy the same slot. The rule SHALL NOT be enforced by a database constraint, so it can change without a schema change; a write that bypasses the lock or the check is a defect. V1 import SHALL re-check its new goals under the lock because it reads existing goals before taking it.

#### Scenario: Racing distinct Rank targets succeed

- **WHEN** two requests concurrently create Ragnar Rank goals with distinct targets
- **THEN** both can commit with distinct valid priorities

## REMOVED Requirements

### Requirement: Rank conflicts name the occupied target

**Reason**: D12 — the 409 payload is specified by "Conflicts identify affected projects and goals" and atomic failure of target edits and transitions by "Every membership and lifecycle path enforces slots"; its scenario repeats `goal-target-editing` "Rank target conflicts in one project".

**Migration**: See `project-goal-slots` — "Conflicts identify affected projects and goals" and "Every membership and lifecycle path enforces slots".
