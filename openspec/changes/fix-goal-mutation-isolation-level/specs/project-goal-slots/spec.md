## ADDED Requirements

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
