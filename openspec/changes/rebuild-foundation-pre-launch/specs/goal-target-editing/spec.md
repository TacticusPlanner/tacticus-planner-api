## MODIFIED Requirements

### Requirement: In-flight adjustable targets can be replaced in place

The `target` section of `PUT /me/goals/{goalId}` SHALL accept `{ expectedRevision, target }` for an owned Active or Paused goal. Supported target groups are Rank (`end`, `endPointFive`, `endAppliedUpgrades`), Ascension (`end` progression), Ability (`activeEnd`, `passiveEnd`), and Upgrade (`targets` material-id/quantity list). Unlock has no adjustable target and SHALL be rejected. The existing start/baseline, entity, goal type, id, status, notes, strategy, sources, project memberships, global position, creation timestamp, and creation snapshot SHALL remain unchanged. A successful response SHALL include the updated goal detail and incremented `revision`.

#### Scenario: Rank target advances in place

- **GIVEN** an Active Bellator Rank goal targets Silver3 in projects A and B
- **WHEN** its owner submits a valid Gold1 end target with the current revision
- **THEN** the same goal id, memberships, order position, status, and creation snapshot remain, and a fresh read returns Gold1 and a higher revision

#### Scenario: All adjustable kinds are supported

- **WHEN** a valid target update is submitted for an owned Active/Paused Ascension, Ability, or Upgrade goal
- **THEN** only that kind's target group changes and the goal identity and unrelated fields remain unchanged

#### Scenario: Nonadjustable or historical goal is refused

- **WHEN** a target update addresses an Unlock, Completed, or Archived goal
- **THEN** the request is rejected without changing the goal

### Requirement: Edited targets receive creation-equivalent validation and slot checks

The new end target SHALL pass the same catalog range, entity/type, target-shape, and dependency-cap rules as creation, evaluated against the goal's persisted baseline and current catalog. A target that is already reached in synced player data SHALL be saved when valid relative to that baseline and current catalog, subject to the same explicit revision and slot-conflict rules as any other valid target; it SHALL have zero remaining need but SHALL not silently change stored lifecycle status. A Rank target SHALL be checked against the profile's other in-flight Rank goals for the unit; an occupied target SHALL return 409 `slotOccupied` naming the existing goal. Validation or conflict failure SHALL be atomic.

#### Scenario: Edit to an already reached target

- **GIVEN** Bellator has progressed beyond Silver3 since a Gold1 goal was created
- **WHEN** its owner edits the end target down to valid Silver3
- **THEN** the edit succeeds, no farming need remains, and the stored status is unchanged until a separate lifecycle action

#### Scenario: Rank target conflicts in one project

- **GIVEN** the profile has another in-flight Bellator Rank goal at the proposed target
- **WHEN** the target edit is submitted
- **THEN** no config changes and the 409 identifies the existing goal and the target

### Requirement: Concurrent edits and target history are explicit

Goal detail SHALL expose a monotonically increasing `revision`. A target update with a stale `expectedRevision` SHALL return 409 `goalRevisionStale` with the current goal detail and SHALL not overwrite it. A successful target change SHALL increment `revision` and SHALL not rewrite the immutable creation snapshot. Retrying an identical target without a change SHALL leave the revision unchanged. No per-change history is stored or served.

#### Scenario: Stale editor cannot overwrite a newer target

- **GIVEN** two editors loaded revision 7 and one saves Gold1 at revision 8
- **WHEN** the other submits a Silver3 edit expecting revision 7
- **THEN** it receives 409 with the current revision-8 target, and no edit is applied

#### Scenario: Target history survives edit

- **WHEN** a valid Rank target is changed from Silver3 to Gold1
- **THEN** the goal detail returns Gold1, a higher revision, and the original creation snapshot unchanged

### Requirement: Editing an Upgrade target can replace its ranges

The `target` section for an Upgrade goal SHALL accept optional `rankRange`, `activeRange` and `passiveRange` groups inside `target.upgrade`, validated exactly as at creation. The Upgrade group SHALL be replaced as a whole: a request that omits a range group clears it. An edit that changes only a range SHALL count as a change and increment `revision`.

#### Scenario: Range edited, targets unchanged

- **GIVEN** an Active Upgrade goal with `rankRange { Stone2, Stone4 }`
- **WHEN** its owner submits the same `targets` with `rankRange { Stone2, Stone5 }` and the current revision
- **THEN** the goal returns the new range and `revision` increments

#### Scenario: Omitting a range clears it

- **WHEN** the owner submits `target.upgrade` with `targets` and no range groups
- **THEN** all three range groups become null

#### Scenario: Invalid edited range is rejected

- **WHEN** the submitted range violates the creation rules
- **THEN** the edit is rejected with the same validation error as creation and nothing changes
