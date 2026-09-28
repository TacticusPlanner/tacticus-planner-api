## MODIFIED Requirements

### Requirement: In-flight adjustable targets can be replaced in place

`PUT /me/goals/{goalId}/target` SHALL accept `{ expectedRevision, target }` for an owned Active or Paused goal. Supported target groups are Rank (`end`, `endPointFive`, `endAppliedUpgrades`), Ascension (`end` progression), Ability (`activeEnd`, `passiveEnd`), and Upgrade (`targets` material-id/quantity list). Unlock has no adjustable target and SHALL be rejected,, and Level is no longer a supported target group (the Level goal type no longer exists), so a request carrying only a Level group is rejected as having no supported group. The existing start/baseline, entity, goal type, id, status, notes, strategy, sources, project memberships, project priorities, creation timestamp, and creation snapshot SHALL remain unchanged. A successful response SHALL include the updated goal detail and incremented `revision`.

#### Scenario: Rank target advances in place

- **GIVEN** an Active Bellator Rank goal targets Silver3 in projects A and B
- **WHEN** its owner submits a valid Gold1 end target with the current revision
- **THEN** the same goal id, memberships, order positions, status, and creation snapshot remain, and a fresh read returns Gold1 and a higher revision

#### Scenario: All adjustable kinds are supported

- **WHEN** a valid target update is submitted for an owned Active/Paused Ascension, Ability, or Upgrade goal
- **THEN** only that kind's target group changes and the goal identity and unrelated fields remain unchanged

#### Scenario: Nonadjustable or historical goal is refused

- **WHEN** a target update addresses an Unlock, Completed, or Archived goal
- **THEN** the request is rejected without changing the goal
