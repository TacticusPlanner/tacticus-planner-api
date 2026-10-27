## MODIFIED Requirements

### Requirement: Rank target identity is the normalized end state

For duplicate detection, a Rank target SHALL be identified by end rank, end point-five state, and end applied-upgrade count after normalization.

#### Scenario: Different targets are distinct

- **GIVEN** one active Bellator Rank goal targets Silver3 clean
- **WHEN** a second Bellator Rank goal targets Gold1 clean in the same project
- **THEN** creation succeeds with a different goal id and both goals remain independently manageable

#### Scenario: Equivalent end target conflicts

- **GIVEN** an active Bellator Rank goal targets Silver3 with three applied slots
- **WHEN** another in-flight Bellator Rank request expresses the same normalized end state through an equivalent point-five representation
- **THEN** it is rejected as a duplicate and identifies the existing goal

## REMOVED Requirements

### Requirement: Deletion frees the active target without restoring stale state

**Reason**: D11 — slot release on delete/complete and fresh identity on recreation are already owned by `goal-target-milestones` and `project-goal-slots`.

**Migration**: See `goal-target-milestones` — "Distinct-target goals are independently managed" and `project-goal-slots` — "In-flight uniqueness is scoped to the profile" (scenario "Historical goal frees slot").
