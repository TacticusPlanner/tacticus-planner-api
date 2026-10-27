## MODIFIED Requirements

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
