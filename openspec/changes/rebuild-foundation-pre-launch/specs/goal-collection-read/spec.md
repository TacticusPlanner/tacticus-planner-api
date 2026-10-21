## Purpose

The goals list and the projects list together are the complete, self-sufficient read model for planning: every goal carries its full detail and project ids, every project carries its goal ids, so a client never fetches per goal or per project.

## ADDED Requirements

### Requirement: The goals list returns full goal detail

`GET /me/goals` SHALL return `{ goals, orderRevision }` where each entry is the same goal detail `GET /me/goals/{goalId}` returns: identity, status, notes, `config`, `snapshot`, `dependsOn`, `projectIds`, `revision`, `globalPriority`, `createdAt`, `updatedAt`. In-flight goals come first in global order, then the rest newest first. `?archived=true` lists only archived goals. No summary shape exists.

#### Scenario: List entry equals detail

- **GIVEN** a goal with a Rank target and memberships in projects A and B
- **WHEN** the caller reads `GET /me/goals` and `GET /me/goals/{goalId}`
- **THEN** the list entry and the detail response are identical, including `config` and `projectIds` `[A, B]`

#### Scenario: Order and revision

- **GIVEN** in-flight goals at positions 1..3 and two Completed goals
- **WHEN** the caller reads the list
- **THEN** the three in-flight goals come first in position order, then the Completed ones newest first, and `orderRevision` is the current order revision

### Requirement: The projects list carries goal ids

Each entry of `GET /me/projects` SHALL carry `goalIds`, the ids of its member goals in global order (in-flight first, then the rest newest first), so membership and counts are derivable without a per-project read. `GET /me/projects/{projectId}/goals` SHALL NOT exist.

#### Scenario: Membership from the projects list

- **GIVEN** project A with members X (position 2) and Y (position 1) and a Completed member Z
- **WHEN** the caller reads `GET /me/projects`
- **THEN** A carries `goalIds` `[Y, X, Z]`

#### Scenario: Membership agrees in both directions

- **WHEN** the caller reads both lists
- **THEN** goal G lists project P in `projectIds` exactly when P lists G in `goalIds`

### Requirement: Goal history is not served

No goal response SHALL carry a per-change event history; `revision`, `createdAt` and `updatedAt` are the only change indicators.

#### Scenario: Detail after several edits

- **GIVEN** a goal paused, resumed and re-targeted
- **WHEN** the caller reads it
- **THEN** the response has no `events` field and `revision` reflects the number of changes
