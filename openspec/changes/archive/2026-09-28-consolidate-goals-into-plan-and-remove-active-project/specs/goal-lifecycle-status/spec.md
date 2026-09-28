## MODIFIED Requirements

### Requirement: Imported V1 goals preserve the source goal's planning choice

The V1 goal import SHALL derive each imported goal's status from that V1 goal's own daily-planning flag rather than from any project: a goal the user had in daily planning SHALL be created `Active`, and a goal they had excluded from it SHALL be created `Paused`. A V1 goal that carries no such flag SHALL be created `Active`, so an absent value never silently pauses an imported goal. The import SHALL NOT offer a caller-supplied paused-creation option — the status comes from the source data, not the request.

A prerequisite the import synthesizes SHALL be `Active` if any imported goal of that unit is `Active`, so that a prerequisite never blocks a goal the user had in daily planning.

#### Scenario: A goal the user had in daily planning imports active

- **GIVEN** the import files goals into the default project
- **WHEN** a V1 profile's goals are imported and a source goal was in daily planning
- **THEN** that imported goal has the `Active` status

#### Scenario: A goal the user had excluded imports paused

- **WHEN** a V1 profile's goals are imported and a source goal was excluded from daily planning
- **THEN** that imported goal has the `Paused` status

#### Scenario: A source goal with no planning flag imports active

- **WHEN** a V1 goal carrying no daily-planning flag is imported
- **THEN** the created goal has the `Active` status

#### Scenario: Synthesized prerequisites follow the goals they serve

- **WHEN** the import synthesizes a missing prerequisite goal for a unit whose imported goals include at least one `Active` goal
- **THEN** that prerequisite is created with the `Active` status

#### Scenario: A prerequisite for wholly paused goals is paused

- **GIVEN** every imported goal for a unit was excluded from daily planning
- **WHEN** the import synthesizes a prerequisite for that unit
- **THEN** that prerequisite is created with the `Paused` status

#### Scenario: Merged duplicates keep the active choice

- **GIVEN** two V1 goals of the same type for the same unit merge into one imported goal, and at least one of them was in daily planning
- **WHEN** the import creates the merged goal
- **THEN** the merged goal has the `Active` status

## ADDED Requirements

### Requirement: Creation status does not depend on project membership

A newly created goal SHALL be given the `Active` status by default, whatever projects it is filed into. Where a creation path has its own explicit source for the status — the caller's paused flag, or an imported goal's own planning choice — that source SHALL decide it; project membership never does.

#### Scenario: Goal created in a custom project is active

- **GIVEN** a custom project B
- **WHEN** the caller creates a goal whose only target project is project B
- **THEN** the created goal has the `Active` status

#### Scenario: Goal created without explicit membership is active

- **WHEN** the caller creates a goal without naming any target project, so it is filed into the default project
- **THEN** the created goal has the `Active` status

#### Scenario: Combined creation produces active goals

- **WHEN** the caller creates a combined set of goals with a dependency chain, targeting project B only
- **THEN** every created goal in the set has the `Active` status

### Requirement: Project membership operations do not change a goal's status

Changing a goal's project memberships and replacing a project's goal membership SHALL leave every affected goal's lifecycle status unchanged. A goal's status SHALL change only through an operation that explicitly targets that goal's status.

#### Scenario: Adding a membership does not change status

- **GIVEN** a `Paused` goal belonging to project A
- **WHEN** the goal is also added to project B, whether through the goal's memberships or through project B's goal membership
- **THEN** the goal keeps the `Paused` status

#### Scenario: Removing a membership does not change status

- **GIVEN** an `Active` goal belonging to projects A and B
- **WHEN** its membership of project A is removed
- **THEN** the goal keeps the `Active` status

## REMOVED Requirements

### Requirement: Creation status is independent of project membership

**Reason**: The active-plan concept is removed; the requirement is restated without it as "Creation status does not depend on project membership".

**Migration**: See the ADDED requirement of that name; behaviour for creation without an active plan is unchanged.

### Requirement: Project operations do not change a goal's status

**Reason**: Changing which project is the active plan no longer exists; the remaining membership scenarios are restated as "Project membership operations do not change a goal's status".

**Migration**: See the ADDED requirement of that name.
