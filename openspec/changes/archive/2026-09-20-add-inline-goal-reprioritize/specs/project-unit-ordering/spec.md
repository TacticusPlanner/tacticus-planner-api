## REMOVED Requirements

### Requirement: Project unit order is addressable through a dedicated operation

**Reason**: Priority is no longer stored or reordered at unit granularity — replaced by a goal-keyed operation (see "Project goal order is addressable through a dedicated operation" under ADDED Requirements below).

**Migration**: Callers of the unit-keyed operation must switch to the new goal-keyed operation, submitting the project's complete ordered list of in-flight goal ids instead of unit `(entityType, entityId)` keys.

### Requirement: Goals inside a unit are ordered automatically

**Reason**: Dependency order is no longer automatically computed or enforced on a goal's priority position. A goal may be positioned anywhere in its project's priority list regardless of whether a goal it `DependsOn` has been reached — priority is now a pure ordering/scheduling preference, fully decoupled from dependency validity. Whether a goal can actually proceed remains a separate, unaffected concern (the existing blocked/restricted signal).

**Migration**: No automatic within-unit ordering exists to migrate to. A caller that relied on goals sharing a unit self-ordering by dependency must now position every goal explicitly via the new goal-order operation (ADDED below); a goal ahead of an unreached prerequisite is accepted, not rejected.

## MODIFIED Requirements

### Requirement: Flattened order remains the canonical scheduler order

Project-goal list responses SHALL return in-flight (Active/Paused) goals in exactly the order most recently established by the goal-order operation, or by append order for a goal never explicitly repositioned. This order SHALL NOT be grouped, re-clustered, or otherwise reordered by unit. Completed/Archived memberships SHALL follow in stable prior order.

#### Scenario: Scheduler receives contiguous unit blocks

- **WHEN** ordered project goals are listed after reprioritization
- **THEN** the returned order exactly matches the most recently submitted goal order — the scheduler no longer receives contiguous per-unit blocks; two goals sharing a unit may have any other goal positioned between them, exactly as submitted

#### Scenario: Scheduler receives the exact submitted order, unit membership notwithstanding

- **WHEN** ordered project goals are listed after a reorder that interleaves goals from different units
- **THEN** the returned order exactly matches the submitted order, with no goal moved to be adjacent to another goal sharing its unit

### Requirement: Goal creation does not accept numeric priority

Single and combined goal creation SHALL accept project membership without caller-authored numeric priority. The API SHALL append each newly created goal to the end of the project's existing in-flight priority order.

#### Scenario: First goal for new unit appends unit

- **WHEN** a goal introduces a unit not already in the project
- **THEN** the new goal is appended after every existing in-flight goal in the project's priority order — there is no separate unit-block position to append to, since order is now flat per-goal

#### Scenario: New goal appends to the end of the list

- **WHEN** a goal is created and added to a project that already has in-flight goals
- **THEN** the new goal is placed after every existing in-flight goal in that project's priority order, regardless of which unit it belongs to

## ADDED Requirements

### Requirement: Project goal order is addressable through a dedicated operation

The API SHALL provide an authenticated operation that accepts the complete ordered set of distinct in-flight (Active/Paused) goal ids currently in an owned project. It SHALL change priority only and SHALL NOT replace membership. It SHALL accept any ordering of the submitted goal ids, including one that places a goal ahead of another goal it `DependsOn`.

#### Scenario: Reorder two goals across different units

- **GIVEN** a project contains an in-flight Ragnar goal and an in-flight Aun'shi goal
- **WHEN** the caller submits the Aun'shi goal before the Ragnar goal
- **THEN** the Aun'shi goal precedes the Ragnar goal in the returned project-goal order

#### Scenario: Stale goal set is rejected atomically

- **GIVEN** project membership changed after the client loaded its in-flight goal list
- **WHEN** it submits a goal id set that is missing an id or includes an id no longer in-flight
- **THEN** the API rejects the request without changing any priorities

#### Scenario: A goal may be positioned ahead of an unreached prerequisite

- **GIVEN** a project contains a Rank goal that `DependsOn` an unreached Ascension goal for the same unit
- **WHEN** the caller submits an order placing the Rank goal before the Ascension goal
- **THEN** the API accepts the order as submitted, and the Rank goal's dependency-blocked state is unaffected by its position

### Requirement: Membership replacement does not accept caller-authored priority

Replacing a project's goal membership (`PUT /me/projects/{id}/goals`) SHALL NOT let the caller set or change a goal's priority. An existing member's priority SHALL remain exactly what it was before the call, regardless of any priority value submitted for it. A newly added member SHALL be appended after the project's current in-flight goals, the same as single goal creation.

#### Scenario: An existing member's priority is unaffected by the request

- **GIVEN** a project membership-replacement request includes a goal that is already a member, with a submitted priority different from its current stored priority
- **WHEN** the request is processed
- **THEN** that goal's priority remains unchanged from before the request

#### Scenario: A newly added member appends

- **GIVEN** a project membership-replacement request adds a goal not previously in the project
- **WHEN** the request is processed
- **THEN** the newly added goal is placed after every existing in-flight goal in the project's priority order, regardless of any priority value submitted for it
