## Purpose

Lets a character have several independently managed Rank milestones while preventing accidental duplicates of the same in-flight target.

## ADDED Requirements

### Requirement: Rank target identity is the normalized end state

For duplicate detection, a Rank target SHALL be identified by end rank, end point-five state, and end applied-upgrade count after normalization; start/baseline and farming strategy SHALL not make the same end target distinct. Distinct targets SHALL retain distinct goal ids, statuses, events, and project memberships.

#### Scenario: Different targets are distinct

- **GIVEN** one active Bellator Rank goal targets Silver3 clean
- **WHEN** a second Bellator Rank goal targets Gold1 clean in the same project
- **THEN** creation succeeds with a different goal id and both goals remain independently manageable

#### Scenario: Equivalent end target conflicts

- **GIVEN** an active Bellator Rank goal targets Silver3 with three applied slots
- **WHEN** another in-flight Bellator Rank request expresses the same normalized end state through an equivalent point-five representation
- **THEN** it is rejected as a duplicate and identifies the existing goal

### Requirement: Deletion frees the active target without restoring stale state

Deleting a Rank goal SHALL release all of its project target occupancy. A later valid recreation SHALL create a new goal id and SHALL NOT resurrect its former memberships, status, or events. Completed/Archived goals SHALL not occupy an in-flight target.

#### Scenario: Delete and recreate

- **WHEN** a user deletes a Bellator Silver3 goal and creates a new Bellator Silver3 goal
- **THEN** the new goal has a new id and only the explicitly requested memberships/defaults

#### Scenario: Historical milestone does not block

- **GIVEN** a completed Bellator Silver3 goal exists
- **WHEN** a new in-flight Bellator Silver3 goal is created
- **THEN** the historical goal does not cause an active-target conflict
