## MODIFIED Requirements

### Requirement: A caller can deliberately create a paused goal

Goal creation (`POST /me/goals`, single or combined) SHALL accept an optional request flag that creates the goals in the `Paused` status instead of `Active`. The flag SHALL default to not paused when it is absent. The flag SHALL apply uniformly to every goal in the request; it SHALL NOT be settable per goal.

#### Scenario: Paused creation is honored

- **WHEN** the caller creates a goal with the paused flag set
- **THEN** the created goal has the `Paused` status

#### Scenario: Omitting the flag creates an active goal

- **WHEN** the caller creates a goal without the paused flag
- **THEN** the created goal has the `Active` status

#### Scenario: Combined creation pauses the whole set

- **WHEN** the caller creates a combined set of goals with the paused flag set
- **THEN** every goal created by that request, including the prerequisite goals of its dependency chain, has the `Paused` status

#### Scenario: A paused new goal still occupies its project slot

- **GIVEN** the profile has an `Active` goal for a unit and goal type
- **WHEN** the caller creates another goal for the same unit and goal type with the paused flag set
- **THEN** the request is rejected as a slot conflict, because `Paused` occupies the slot exactly as `Active` does

### Requirement: Project membership operations do not change a goal's status

Changing a goal's project memberships and replacing a project's goal membership SHALL leave every affected goal's lifecycle status unchanged. A goal's status SHALL change only through `POST /me/goals/status` (see `goal-bulk-status`), which explicitly names the goals it transitions.

#### Scenario: Adding a membership does not change status

- **GIVEN** a `Paused` goal belonging to project A
- **WHEN** the goal is also added to project B, whether through the goal's memberships or through project B's goal membership
- **THEN** the goal keeps the `Paused` status

#### Scenario: Removing a membership does not change status

- **GIVEN** an `Active` goal belonging to projects A and B
- **WHEN** its membership of project A is removed
- **THEN** the goal keeps the `Active` status

## REMOVED Requirements

### Requirement: Existing goal statuses are not rewritten

**Reason**: The database is recreated with no data carried over; there are no previously persisted statuses to preserve.

**Migration**: None.
