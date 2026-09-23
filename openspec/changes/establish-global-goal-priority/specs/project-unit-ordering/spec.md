## MODIFIED Requirements

### Requirement: Flattened order remains the canonical scheduler order

Project-goal list responses SHALL return in-flight (Active/Paused) goals as a filtered projection of the account-wide order, without regrouping by unit. Completed/Archived memberships SHALL follow in stable goal-creation-time and goal-ID order and SHALL not report an in-flight position.

#### Scenario: Scheduler receives contiguous unit blocks
- **WHEN** a project's global-priority projection is listed after account-wide reprioritization
- **THEN** the returned order follows global positions; two goals sharing a unit may have another goal between them

#### Scenario: Scheduler receives the exact submitted order, unit membership notwithstanding
- **WHEN** account-wide order interleaves different units in one project
- **THEN** the project read preserves that interleaving

### Requirement: Goal creation does not accept numeric priority

Single and combined goal creation SHALL accept project membership without caller-authored numeric priority. The API SHALL append each newly created goal once to the account's in-flight priority order, even if it belongs to multiple projects.

#### Scenario: First goal for new unit appends unit
- **WHEN** a goal introduces a unit not already represented in the account's in-flight order
- **THEN** it appends after all existing in-flight goals, without a unit block

#### Scenario: New goal appends to the end of the list
- **WHEN** a goal is created for a unit already represented in another project
- **THEN** it appends to the account-wide order regardless of unit or project

### Requirement: Project goal order is addressable through a dedicated operation

Goal order SHALL be addressable only through the account-wide operation specified by `global-goal-priority`. The project-scoped order operation SHALL no longer accept reorder requests. Reordering SHALL not replace membership and SHALL not enforce dependency position.

#### Scenario: Reorder two goals across different units
- **WHEN** the owner submits the Aun'shi goal before the Ragnar goal through the global operation
- **THEN** Aun'shi precedes Ragnar in global and applicable project reads

#### Scenario: Stale goal set is rejected atomically
- **WHEN** a global order request omits a newly created in-flight goal
- **THEN** it is rejected without changing priorities

#### Scenario: A goal may be positioned ahead of an unreached prerequisite
- **WHEN** a global order request places a Rank goal before its unreached Ascension dependency
- **THEN** the order is accepted and the dependency-blocked state is unaffected

### Requirement: Membership replacement does not accept caller-authored priority

Replacing a project's goal membership (`PUT /me/projects/{id}/goals`) SHALL NOT let the caller set or change global priority. Existing members and newly added members SHALL retain their account-wide positions. A newly created goal receives a global position through creation, not through later membership replacement.

#### Scenario: An existing member's priority is unaffected by the request
- **WHEN** membership replacement retains a goal and submits a legacy priority value
- **THEN** its global priority remains unchanged and the submitted value is ignored or rejected as invalid contract input

#### Scenario: A newly added member appends
- **WHEN** membership replacement adds an existing goal to a project
- **THEN** that goal appears at its existing global position in the project's filtered list, not at the end of global order
