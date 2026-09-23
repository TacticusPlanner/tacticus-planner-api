## Purpose

Defines one account-owned order for all in-flight goals so planning consumers cannot produce different execution priorities from project membership or selection.

## ADDED Requirements

### Requirement: Account has one canonical in-flight order

Each owned Active or Paused goal SHALL have exactly one account-wide priority position, regardless of its project memberships or whether its unit is a Character or Machine of War. The order SHALL contain each goal ID once. Completed and Archived goals SHALL remain readable but SHALL not occupy an in-flight position. `GET /me/goals` SHALL expose the canonical position and an order revision; listing a project SHALL preserve the same relative positions after filtering.

#### Scenario: Goal shared by projects
- **WHEN** one goal belongs to two projects
- **THEN** global reads contain it once and both project reads report the same priority

#### Scenario: Mixed unit types
- **WHEN** a Character goal is placed between two Machine-of-War goals
- **THEN** reads return exactly that interleaving, without unit regrouping

### Requirement: Owner can reorder the complete global set

`PUT /me/goals/order` SHALL accept the complete distinct set of the owner's current Active and Paused goal IDs in desired order and an expected order revision. It SHALL change only priority, not goal status, dependencies, targets, or membership. The server SHALL reject missing, duplicate, foreign, terminal, or newly added IDs and stale revisions atomically with a structured conflict response. Dependency order SHALL not be enforced by this operation.

#### Scenario: Reorder across projects
- **WHEN** an owner submits a complete valid set moving a goal in project B ahead of one in project A
- **THEN** both the global read and filtered project reads reflect the new relative positions

#### Scenario: Concurrent addition
- **GIVEN** a client loaded the order before another operation created a goal
- **WHEN** the client submits its old set and revision
- **THEN** the reorder is rejected without changing any positions

#### Scenario: Dependency appears after its dependent
- **WHEN** the submitted order places a dependent Rank goal ahead of its Ascension prerequisite
- **THEN** the order is accepted and the dependency remains unchanged

### Requirement: Lifecycle changes maintain global order

New Active or Paused goals SHALL append once to the global in-flight order, regardless of project count. Pausing SHALL retain a goal's position; resuming SHALL retain its position if it remained Paused. Completing or archiving SHALL remove its in-flight position; returning a terminal goal to an in-flight status SHALL append it. Deleting a goal SHALL remove it. Membership edits and changing the Current plan SHALL not change global positions. Every change to the in-flight set or order SHALL advance the order revision.

#### Scenario: Combined creation across projects
- **WHEN** a combined creation makes three goals and files each into two projects
- **THEN** the three goals append in request order once each, not six times

#### Scenario: Pause and resume
- **WHEN** a goal is Paused and later resumed
- **THEN** it occupies its prior position throughout

#### Scenario: Complete then reopen
- **WHEN** a Completed goal is moved back to Active
- **THEN** it appends after current in-flight goals

#### Scenario: Membership change
- **WHEN** a goal is added to or removed from a non-last project
- **THEN** its global position and order revision do not change

### Requirement: Existing orders migrate deterministically

For an existing account, migration SHALL start with the in-flight goals of the former Current plan in their stored project order. It SHALL then append previously unseen in-flight goals from other projects ordered by project creation time and project ID, using each project's stored goal order and goal ID as tie-breakers. Remaining in-flight goals without a project SHALL append by goal creation time and goal ID. A goal shared across projects SHALL appear at its first encounter only. Historical goals SHALL preserve status and history without entering the order. Migration SHALL not use `updatedAt` as a priority source.

#### Scenario: Shared goal and outside-project goal
- **GIVEN** Current plan A orders X then Y, and older project B orders Y then Z
- **WHEN** the account migrates
- **THEN** the global order is X, Y, Z

#### Scenario: No Current plan
- **WHEN** an account has projects but no Current plan
- **THEN** all projects contribute in creation-time and ID order with goal-order tie-breaks
