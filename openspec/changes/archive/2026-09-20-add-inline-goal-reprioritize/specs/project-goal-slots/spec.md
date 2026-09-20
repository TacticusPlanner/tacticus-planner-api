## MODIFIED Requirements

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
