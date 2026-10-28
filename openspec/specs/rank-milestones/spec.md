# rank-milestones Specification

## Purpose

Lets a character have several independently managed Rank milestones while preventing accidental duplicates of the same in-flight target.

## Requirements

### Requirement: Rank target identity is the normalized end state

For duplicate detection, a Rank target SHALL be identified by end rank, end point-five state, and end applied-upgrade count after normalization. A goal created for a target whose earlier goal was deleted SHALL get a new goal id and SHALL NOT restore the deleted goal's status or project memberships.

#### Scenario: Equivalent end target conflicts

- **GIVEN** an active Bellator Rank goal targets Silver3 with three applied slots
- **WHEN** another in-flight Bellator Rank request expresses the same normalized end state through an equivalent point-five representation
- **THEN** it is rejected as a duplicate and identifies the existing goal
