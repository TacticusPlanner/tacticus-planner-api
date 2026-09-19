# goal-lifecycle-status Specification

## Purpose
Establishes that a goal's lifecycle status belongs to the goal itself rather than
to its project memberships: what status each creation path produces, how a caller
deliberately creates a paused goal, and which project operations are forbidden
from changing a goal's status.

## Requirements

### Requirement: Creation status is independent of project membership

A newly created goal SHALL be given the `Active` status by default, whatever
projects it is filed into and whatever project the profile currently records as
its active plan. No creation path SHALL consult the profile's active project to
decide a new goal's status. Where a creation path has its own explicit source for
the status — the caller's paused flag, or an imported goal's own planning choice
— that source SHALL decide it; project membership never does.

#### Scenario: Goal created in a non-current project is active

- **GIVEN** the caller's active plan is project A
- **WHEN** the caller creates a goal whose only target project is project B
- **THEN** the created goal has the `Active` status

#### Scenario: Goal created in the current plan is active

- **GIVEN** the caller's active plan is project A
- **WHEN** the caller creates a goal targeting project A
- **THEN** the created goal has the `Active` status

#### Scenario: Goal created without explicit membership is active

- **GIVEN** the caller's active plan is a project other than the default project
- **WHEN** the caller creates a goal without naming any target project, so it is
  filed into the default project
- **THEN** the created goal has the `Active` status

#### Scenario: Combined creation produces active goals

- **GIVEN** the caller's active plan is project A
- **WHEN** the caller creates a combined set of goals with a dependency chain,
  targeting project B only
- **THEN** every created goal in the set has the `Active` status

#### Scenario: The profile's active plan is unchanged by creation

- **WHEN** a goal is created into any project
- **THEN** the profile's recorded active plan is the same project it was before
  the request

### Requirement: A caller can deliberately create a paused goal

Single and combined goal creation SHALL accept an optional request flag that
creates the goal in the `Paused` status instead of `Active`. The flag SHALL
default to not paused when it is absent, so a request that omits it creates an
`Active` goal. For combined creation the flag SHALL apply uniformly to every goal
in the request; the flag SHALL NOT be settable per goal within one combined
request.

#### Scenario: Paused creation is honored

- **WHEN** the caller creates a goal with the paused flag set
- **THEN** the created goal has the `Paused` status

#### Scenario: Omitting the flag creates an active goal

- **WHEN** the caller creates a goal without the paused flag
- **THEN** the created goal has the `Active` status

#### Scenario: Combined creation pauses the whole set

- **WHEN** the caller creates a combined set of goals with the paused flag set
- **THEN** every goal created by that request, including the prerequisite goals
  of its dependency chain, has the `Paused` status

#### Scenario: A paused new goal still occupies its project slot

- **GIVEN** project A contains an `Active` goal for a unit and goal type
- **WHEN** the caller creates another goal for the same unit and goal type in
  project A with the paused flag set
- **THEN** the request is rejected as a project goal-slot conflict, because
  `Paused` occupies the slot exactly as `Active` does

### Requirement: Imported V1 goals preserve the source goal's planning choice

The V1 goal import SHALL derive each imported goal's status from that V1 goal's
own daily-planning flag rather than from any project: a goal the user had in
daily planning SHALL be created `Active`, and a goal they had excluded from it
SHALL be created `Paused`. A V1 goal that carries no such flag SHALL be created
`Active`, so an absent value never silently pauses an imported goal. The import
SHALL NOT consult the profile's active plan, and SHALL NOT offer a caller-supplied
paused-creation option — the status comes from the source data, not the request.

A prerequisite the import synthesizes SHALL be `Active` if any imported goal of
that unit is `Active`, so that a prerequisite never blocks a goal the user had in
daily planning.

#### Scenario: A goal the user had in daily planning imports active

- **GIVEN** the caller's active plan is a project other than the default project,
  and the import files goals into the default project
- **WHEN** a V1 profile's goals are imported and a source goal was in daily
  planning
- **THEN** that imported goal has the `Active` status

#### Scenario: A goal the user had excluded imports paused

- **WHEN** a V1 profile's goals are imported and a source goal was excluded from
  daily planning
- **THEN** that imported goal has the `Paused` status

#### Scenario: A source goal with no planning flag imports active

- **WHEN** a V1 goal carrying no daily-planning flag is imported
- **THEN** the created goal has the `Active` status

#### Scenario: Synthesized prerequisites follow the goals they serve

- **WHEN** the import synthesizes a missing prerequisite goal for a unit whose
  imported goals include at least one `Active` goal
- **THEN** that prerequisite is created with the `Active` status

#### Scenario: A prerequisite for wholly paused goals is paused

- **GIVEN** every imported goal for a unit was excluded from daily planning
- **WHEN** the import synthesizes a prerequisite for that unit
- **THEN** that prerequisite is created with the `Paused` status

#### Scenario: Merged duplicates keep the active choice

- **GIVEN** two V1 goals of the same type for the same unit merge into one
  imported goal, and at least one of them was in daily planning
- **WHEN** the import creates the merged goal
- **THEN** the merged goal has the `Active` status

### Requirement: Project operations do not change a goal's status

Changing a goal's project memberships, replacing a project's goal membership, and
changing which project is the profile's active plan SHALL all leave every
affected goal's lifecycle status unchanged. A goal's status SHALL change only
through an operation that explicitly targets that goal's status.

#### Scenario: Making a project current does not activate its goals

- **GIVEN** project B contains `Paused` goals and project A is the active plan
- **WHEN** the caller makes project B the active plan
- **THEN** project B's goals keep the `Paused` status

#### Scenario: Losing current-plan standing does not pause goals

- **GIVEN** project A is the active plan and contains `Active` goals
- **WHEN** the caller makes project B the active plan
- **THEN** project A's goals keep the `Active` status

#### Scenario: Adding a membership does not change status

- **GIVEN** a `Paused` goal belonging to project A
- **WHEN** the goal is also added to project B, whether through the goal's
  memberships or through project B's goal membership
- **THEN** the goal keeps the `Paused` status

#### Scenario: Removing a membership does not change status

- **GIVEN** an `Active` goal belonging to projects A and B
- **WHEN** its membership of project A is removed
- **THEN** the goal keeps the `Active` status

### Requirement: Existing goal statuses are not rewritten

Adopting membership-independent creation SHALL NOT alter any already-persisted
goal's status. A goal that is `Paused` when this behavior takes effect SHALL
remain `Paused` until a user acts on it, because a status recorded under the
previous rule is indistinguishable from one a user chose deliberately.

#### Scenario: Previously paused goals stay paused

- **GIVEN** goals persisted with the `Paused` status before this behavior took
  effect
- **WHEN** the system starts with the new creation behavior in place
- **THEN** those goals still have the `Paused` status and no bulk status change
  has been applied
