# global-goal-priority Specification

## Purpose
Defines one account-owned order for all in-flight goals so planning consumers cannot produce different execution priorities from project membership or selection.

## Requirements

### Requirement: Account has one canonical in-flight order

Each owned Active or Paused goal SHALL have exactly one account-wide priority position, regardless of its project memberships or whether its unit is a Character or Machine of War. The order SHALL contain each goal ID once. Completed and Archived goals SHALL remain readable but SHALL not occupy an in-flight position. `GET /goals` SHALL expose the canonical position and an order revision; listing a project SHALL preserve the same relative positions after filtering.

#### Scenario: Goal shared by projects
- **WHEN** one goal belongs to two projects
- **THEN** global reads contain it once and both project reads report the same priority

#### Scenario: Mixed unit types
- **WHEN** a Character goal is placed between two Machine-of-War goals
- **THEN** reads return exactly that interleaving, without unit regrouping

### Requirement: Owner can reorder the complete global set

`PUT /goals/order` SHALL accept the complete distinct set of the owner's current Active and Paused goal IDs in desired order and an expected order revision. It SHALL change only priority, not goal status, dependencies, targets, or membership. The server SHALL reject missing, duplicate, foreign, terminal, or newly added IDs and stale revisions atomically with a structured conflict response. Dependency order SHALL not be enforced by this operation.

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

### Requirement: Owner can move a goal within a project's projection

`PUT /projects/{projectId}/goal-order` SHALL accept a goal ID, a displaced goal ID and the expected order revision. Both goals SHALL be in-flight members of that project. The server SHALL move the goal to the global position currently held by the displaced goal; every in-flight goal between the two positions, project member or not, SHALL shift one place toward the vacated position, and all other relative positions SHALL be unchanged. The operation SHALL change only priority and SHALL return the new order revision and canonical ordered IDs. It SHALL reject a goal moved onto itself, non-members, non-in-flight or foreign goals, and stale revisions atomically, the stale case with the same structured conflict as the global reorder.

#### Scenario: Moving a member up within the project
- **GIVEN** global order A, B, C, D, E and a project containing A, C and E
- **WHEN** the owner moves E onto C
- **THEN** the global order is A, B, E, C, D and the project projection reads A, E, C

#### Scenario: Moving a member down within the project
- **GIVEN** global order A, B, C, D, E and a project containing A, C and E
- **WHEN** the owner moves C onto E
- **THEN** the global order is A, B, D, E, C and the project projection reads A, E, C

#### Scenario: Non-member goals keep their relative order
- **WHEN** a member is moved past several non-member goals
- **THEN** the non-member goals appear in the same relative order as before, and other projects' projections change only where the moved goal crossed their members

#### Scenario: Invalid move
- **WHEN** the displaced goal is not an in-flight member of the project, or equals the moved goal
- **THEN** the request is rejected and no position or revision changes

#### Scenario: Stale revision
- **WHEN** the order changed after the client loaded it
- **THEN** the move is rejected with the structured conflict and no positions change

### Requirement: Lifecycle changes maintain global order

New Active or Paused goals SHALL enter the global in-flight order once each, regardless of project count. A creation request without `position` (or with a null one) SHALL append its goals at the end in request order. A creation request with `position` SHALL place its goals at `position`, `position + 1`, ... in request order, and every existing goal at or after `position` SHALL shift down by the number created; `position` SHALL be between 1 and the in-flight count + 1, read under the account's lock, and otherwise the request SHALL be a 400 validation problem naming `position` that creates nothing. Pausing SHALL retain a goal's position; resuming SHALL retain its position if it remained Paused. Completing or archiving SHALL remove its in-flight position; returning a terminal goal to an in-flight status SHALL append it. Deleting a goal SHALL remove it. Membership edits SHALL not change global positions. Every change to the in-flight set or order SHALL advance the order revision.

#### Scenario: Combined creation across projects
- **WHEN** a combined creation makes three goals and files each into two projects
- **THEN** the three goals append in request order once each, not six times

#### Scenario: Creation at the top
- **WHEN** the owner has three in-flight goals and creates two goals with `position` 1
- **THEN** the new goals hold positions 1 and 2 in request order and the earlier goals hold 3 to 5 in their previous relative order

#### Scenario: Creation position out of range
- **WHEN** the owner has three in-flight goals and creates goals with `position` 0 or 5
- **THEN** the response is 400 `validation` naming `position`, and no goal is created and no position changes

#### Scenario: Pause and resume
- **WHEN** a goal is Paused and later resumed
- **THEN** it occupies its prior position throughout

#### Scenario: Complete then reopen
- **WHEN** a Completed goal is moved back to Active
- **THEN** it appends after current in-flight goals

#### Scenario: Membership change
- **WHEN** a goal is added to or removed from a non-last project
- **THEN** its global position and order revision do not change
