## Purpose

Allows a user to revise an in-flight goal's progression target without losing its identity, membership, order, or historical creation baseline.

## ADDED Requirements

### Requirement: In-flight adjustable targets can be replaced in place

`PUT /me/goals/{goalId}/target` SHALL accept `{ expectedRevision, target }` for an owned Active or Paused goal. Supported target groups are Rank (`end`, `endPointFive`, `endAppliedUpgrades`), Ascension (`end` progression), Level (`end` level), Ability (`activeEnd`, `passiveEnd`), and Upgrade (`targets` material-id/quantity list). Unlock has no adjustable target and SHALL be rejected. The existing start/baseline, entity, goal type, id, status, notes, strategy, sources, project memberships, project priorities, creation timestamp, and creation snapshot SHALL remain unchanged. A successful response SHALL include the updated goal detail and incremented `revision`.

#### Scenario: Rank target advances in place

- **GIVEN** an Active Bellator Rank goal targets Silver3 in projects A and B
- **WHEN** its owner submits a valid Gold1 end target with the current revision
- **THEN** the same goal id, memberships, order positions, status, and creation snapshot remain, and a fresh read returns Gold1 and a higher revision

#### Scenario: All adjustable kinds are supported

- **WHEN** a valid target update is submitted for an owned Active/Paused Ascension, Level, Ability, or Upgrade goal
- **THEN** only that kind's target group changes and the goal identity and unrelated fields remain unchanged

#### Scenario: Nonadjustable or historical goal is refused

- **WHEN** a target update addresses an Unlock, Completed, or Archived goal
- **THEN** the request is rejected without changing the goal

### Requirement: Edited targets receive creation-equivalent validation and slot checks

The new end target SHALL pass the same catalog range, entity/type, target-shape, and dependency-cap rules as creation, evaluated against the goal's persisted baseline and current catalog. A target that is already reached in synced player data MAY be saved if valid relative to that baseline; it SHALL have zero remaining need but SHALL not silently change stored lifecycle status. Rank target identity SHALL be checked in every membership under `support-multiple-rank-milestones`; an occupied target SHALL return structured 409 naming every conflicting project and existing goal. Validation or conflict failure SHALL be atomic.

#### Scenario: Edit to an already reached target

- **GIVEN** Bellator has progressed beyond Silver3 since a Gold1 goal was created
- **WHEN** its owner edits the end target down to valid Silver3
- **THEN** the edit succeeds, no farming need remains, and the stored status is unchanged until a separate lifecycle action

#### Scenario: Rank target conflicts in one project

- **GIVEN** the edited goal belongs to A and B and B already contains another in-flight goal at its proposed Rank target
- **WHEN** the target edit is submitted
- **THEN** no config, membership key, or event changes and the 409 identifies B and the existing goal

### Requirement: Concurrent edits and target history are explicit

Goal detail SHALL expose a monotonically increasing `revision`. A target update with a stale `expectedRevision` SHALL return 409 with the current goal detail and SHALL not overwrite it. Every successful target change SHALL append an event with timestamp, previous target, and new target; it SHALL not rewrite the immutable creation snapshot. Retrying an identical target without a change SHALL leave revision and history unchanged.

#### Scenario: Stale editor cannot overwrite a newer target

- **GIVEN** two editors loaded revision 7 and one saves Gold1 at revision 8
- **WHEN** the other submits a Silver3 edit expecting revision 7
- **THEN** it receives 409 with the current revision-8 target, and no edit is applied

#### Scenario: Target history survives edit

- **WHEN** a valid Rank target is changed from Silver3 to Gold1
- **THEN** goal detail contains a TargetChanged event recording both end targets and time, while the original snapshot is preserved
