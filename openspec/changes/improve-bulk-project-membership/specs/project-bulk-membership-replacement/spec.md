## Purpose

Lets a client review and replace multiple memberships in one owned project without silently overwriting concurrent edits or changing goal priority.

## ADDED Requirements

### Requirement: Replacement checks the membership snapshot
The authenticated project membership replacement operation SHALL accept the complete desired distinct goal-ID set and the complete distinct goal-ID set the client reviewed. Under the same project lock/transaction used for replacement, it SHALL compare the reviewed set with current membership. A mismatch SHALL reject the whole request with a structured stale-membership conflict containing current goal IDs; no membership or priority SHALL change.

#### Scenario: Fresh reviewed set
- **WHEN** an owned project's reviewed set matches current membership and the desired set passes validation
- **THEN** the complete desired set is committed atomically

#### Scenario: Another edit happened first
- **WHEN** a membership is added or removed after the client reviewed the set but before its replacement acquires the lock
- **THEN** the replacement is rejected as stale with the current IDs and no partial add/remove

### Requirement: Batch validation is atomic and actionable
The operation SHALL reject an unknown/unowned goal, an occupied in-flight project slot, or a removal that would leave a goal with no project, without applying any part of the desired set. Slot conflicts SHALL retain the existing structured project/goal response. Last-membership rejection SHALL identify the blocked goal IDs for client review.

#### Scenario: One blocked removal among valid additions
- **WHEN** a desired set adds two valid goals but removes a third goal from its only project
- **THEN** no change commits and the response identifies that blocked goal

#### Scenario: Occupied slot
- **WHEN** two desired in-flight goals occupy the same project/unit/type slot
- **THEN** no change commits and the existing structured slot-conflict response identifies the conflict

### Requirement: Membership save does not reorder global goals
Replacing a project's membership SHALL leave each goal's canonical account-wide priority unchanged, including goals added or removed from the project. Project views SHALL subsequently project the canonical order rather than deriving an execution order from membership mutation.

#### Scenario: Add and remove without reprioritizing
- **WHEN** a valid batch adds and removes memberships after global priority is established
- **THEN** the account-wide goal order before and after the replacement is identical
