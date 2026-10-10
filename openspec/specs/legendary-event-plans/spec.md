# legendary-event-plans Specification

## Purpose

Defines the persisted Legendary Event plan per profile and event: its teams (lane, members, reserve, covered objectives, clear depth per run, order), the plan-level fields, how every write is validated against the Game Catalog, the single-revision concurrency contract, and the served shape every plan endpoint returns.

## Requirements

### Requirement: One plan per profile and Legendary Event

The system SHALL persist at most one plan per profile and catalog Legendary Event id. A plan SHALL carry a monotonic `revision`, the catalog version it was last written under (`catalogVersion`), optional `notes` (at most 2000 characters) and a `showPaidOptions` flag (default false). Plans SHALL be scoped to the caller's profile: a plan is never readable or writable by another profile, and deleting the account removes its plans and everything beneath them.

#### Scenario: A missing plan reads as an empty plan

- **GIVEN** a profile with no plan for event `astarLysander`
- **WHEN** the caller requests `GET /me/legendary-event-plans/astarLysander`
- **THEN** the response is 200 with `eventId` `astarLysander`, `revision` 0, `notes` null, `showPaidOptions` false, an empty `teams` list and the current `catalogVersion`, and no row is created

#### Scenario: Unknown event id

- **WHEN** the caller requests or writes `/me/legendary-event-plans/{eventId}` for an id that is not a current catalog `lres` id
- **THEN** the response is 404

#### Scenario: Plan-level write creates the plan

- **GIVEN** no plan exists for the event
- **WHEN** the caller sends `PUT /me/legendary-event-plans/{eventId}` with `expectedRevision` 0, `notes` "Alpha first" and `showPaidOptions` true
- **THEN** the plan is created, the response is 200 with `revision` 1, the given fields and the current `catalogVersion`

#### Scenario: Account purge cascades

- **GIVEN** a profile with a plan that has teams, members and objectives
- **WHEN** the account is deleted
- **THEN** the plan and all of its teams, members and objectives are removed

### Requirement: Teams belong to one lane of the plan

A team SHALL belong to exactly one lane (`alpha`, `beta` or `gamma`) of its plan and SHALL carry a trimmed `name` of 1–60 characters, an ordered list of 1–5 member unit ids, at most one reserve unit id, a set of covered objective indexes, at most one clear depth per run 1–3 (`expectedBattleClears` with its `expectedBattleClearsSource`, `estimate` or `manual`, and the time it was last written), and a `sortOrder` that is dense and 0-based within its lane. No unit SHALL appear twice in one team (members and reserve together). A team MAY cover zero objectives.

#### Scenario: Create a team

- **GIVEN** a plan at revision 1 for an event whose alpha lane allows units `u1`…`u6` and has objectives with indexes 0–4
- **WHEN** the caller sends `POST …/teams` with `expectedRevision` 1, `laneId` `alpha`, `name` "Melee", `memberUnitIds` `[u1,u2,u3]`, `reserveUnitId` `u4`, `objectiveIndexes` `[0,3]`, `run` 1, `expectedBattleClears` 7 and `expectedBattleClearsSource` `manual`
- **THEN** the response is 200 with the whole plan at revision 2, containing the new team with a server-generated id, `sortOrder` 0, the given fields and `runDepths` `[{run 1, 7, manual}]`

#### Scenario: A team is appended after the lane's existing teams

- **GIVEN** the alpha lane already has teams at `sortOrder` 0 and 1
- **WHEN** a team is created on alpha
- **THEN** it has `sortOrder` 2 and the existing orders are unchanged

#### Scenario: Update a team

- **GIVEN** a team on alpha at plan revision 2
- **WHEN** the caller sends `PUT …/teams/{teamId}` with `expectedRevision` 2 and new members, objectives, name and a depth for `run` 1
- **THEN** the team's lane and `sortOrder` are unchanged, the other fields are replaced, and the response is the plan at revision 3

#### Scenario: A depth write touches only its run

- **GIVEN** a team with `runDepths` `[{run 1, 7, manual}]`
- **WHEN** the caller sends `PUT …/teams/{teamId}` with `run` 2, `expectedBattleClears` 9 and `expectedBattleClearsSource` `manual`
- **THEN** the team's `runDepths` is `[{run 1, 7, manual}, {run 2, 9, manual}]`

#### Scenario: A null depth clears only its run

- **GIVEN** a team with depths for runs 1 and 2
- **WHEN** the caller sends `PUT …/teams/{teamId}` with `run` 2 and `expectedBattleClears` null
- **THEN** the team's `runDepths` is `[{run 1, …}]`

#### Scenario: Lane cannot change on update

- **WHEN** a `PUT …/teams/{teamId}` body carries a `laneId`
- **THEN** the response is 400 naming `laneId`

#### Scenario: Delete a team re-densifies its lane

- **GIVEN** alpha teams A, B, C at orders 0, 1, 2
- **WHEN** the caller deletes B with `DELETE …/teams/{teamId}?expectedRevision=<current>`
- **THEN** A and C have orders 0 and 1 and the response is the plan at the next revision

#### Scenario: Unknown team id

- **WHEN** `PUT` or `DELETE …/teams/{teamId}` names a team that is not on this plan
- **THEN** the response is 404

### Requirement: Every team write is validated against the current Game Catalog

On create and update the system SHALL reject, with 400 and the offending field name, a `laneId` outside `alpha|beta|gamma`; a member or reserve unit id not present in that lane's `availableUnitIds`; a repeated unit; an `objectiveIndexes` entry that is not an `index` of that lane's `unitsRestrictions`, or a repeated index; a `run` outside 1..3; an `expectedBattleClears` outside 1..(battle count of the lane); a non-null depth without a source, or a source without a depth; an empty or over-long name. The plan's `catalogVersion` SHALL be set to the current catalog version on every successful write.

#### Scenario: Unit not allowed on the lane

- **GIVEN** unit `u9` is not in the alpha lane's `availableUnitIds`
- **WHEN** a team is created on alpha with `u9` as a member
- **THEN** the response is 400 naming `memberUnitIds` and no team is created

#### Scenario: Objective index outside the lane

- **WHEN** a team on alpha carries `objectiveIndexes` `[5]` and the lane has indexes 0–4
- **THEN** the response is 400 naming `objectiveIndexes`

#### Scenario: Depth outside the lane

- **GIVEN** the lane has 18 battles
- **WHEN** a team carries `expectedBattleClears` 19
- **THEN** the response is 400 naming `expectedBattleClears`

#### Scenario: Run outside the event

- **WHEN** a team carries `run` 4
- **THEN** the response is 400 naming `run`

#### Scenario: Depth and source travel together

- **WHEN** a team carries `expectedBattleClears` 5 and `expectedBattleClearsSource` null
- **THEN** the response is 400 naming `expectedBattleClearsSource`

#### Scenario: Reads never fail on catalog drift

- **GIVEN** a plan written under an earlier catalog version whose objective indexes no longer validate
- **WHEN** the plan is read
- **THEN** the response is 200 with the stored ids and the stored `catalogVersion`

### Requirement: Lane order is replaced as a whole set

`PUT …/teams/order` SHALL take `expectedRevision`, `laneId` and `teamIds`, where `teamIds` is the complete set of that lane's team ids in the desired order. The system SHALL assign dense 0-based `sortOrder` in that order and bump the revision. When `teamIds` has duplicates, or its set differs from the lane's current team set, the response SHALL be 409 with `issueCode` `legendaryEventOrderSetMismatch` and the current plan. A reorder that changes nothing SHALL still succeed and SHALL NOT bump the revision.

#### Scenario: Reorder a lane

- **GIVEN** alpha teams A, B, C at orders 0, 1, 2 and plan revision 4
- **WHEN** the caller sends `PUT …/teams/order` with `laneId` `alpha` and `teamIds` `[C, A, B]`
- **THEN** C, A, B have orders 0, 1, 2 and the response is the plan at revision 5

#### Scenario: Set mismatch

- **WHEN** the caller sends `teamIds` `[A, B]` for a lane that has A, B and C
- **THEN** the response is 409 with `issueCode` `legendaryEventOrderSetMismatch` and the current plan, and nothing changes

#### Scenario: Unchanged order keeps the revision

- **WHEN** the caller sends the lane's current order
- **THEN** the response is 200 with the plan at the same revision

### Requirement: One revision per plan guards every mutation

Every mutation (`PUT` plan, `POST`/`PUT`/`DELETE` team, `PUT` order) SHALL carry `expectedRevision`. When it differs from the plan's current revision (0 for a plan that does not exist yet), the system SHALL make no change and respond 409 with `LegendaryEventPlanConflictResponse { issueCode: "legendaryEventPlanStale", message, plan }` carrying the current plan. A concurrent write detected at save time, including a unique-key violation when two callers create the missing plan at once, SHALL produce the same response after re-reading the plan. Every successful mutation SHALL bump the plan revision by exactly one and respond with the whole plan, with one exception: a `PUT …/teams/order` whose order is already current succeeds without bumping the revision (see "Lane order is replaced as a whole set").

#### Scenario: Stale revision

- **GIVEN** a plan at revision 3
- **WHEN** the caller creates a team with `expectedRevision` 2
- **THEN** the response is 409 with `issueCode` `legendaryEventPlanStale` and the plan at revision 3, and no team is created

#### Scenario: Lazy creation requires revision 0

- **GIVEN** no plan exists for the event
- **WHEN** a team is created with `expectedRevision` 1
- **THEN** the response is 409 with the empty revision-0 plan

#### Scenario: Concurrent first writers

- **GIVEN** no plan exists for the event
- **WHEN** two callers each create a team with `expectedRevision` 0 at the same time
- **THEN** exactly one plan row exists, one caller succeeds with revision 1 and the other receives 409 carrying that plan

#### Scenario: Concurrent writers

- **GIVEN** two callers hold the plan at revision 3
- **WHEN** both send a team create with `expectedRevision` 3
- **THEN** exactly one succeeds with revision 4 and the other receives 409 carrying the plan at revision 4

### Requirement: Served plan shape

Every plan endpoint SHALL respond with the same `LegendaryEventPlanResponse`: `eventId`, `revision`, `catalogVersion`, `notes`, `showPaidOptions` and `teams` ordered by lane (`alpha`, `beta`, `gamma`) then `sortOrder`. Each team SHALL carry `id`, `laneId`, `name`, `sortOrder`, `memberUnitIds` in position order, `reserveUnitId` (null when none), `objectiveIndexes` ascending, and `runDepths` ascending by run with one entry per stored run (`run`, `expectedBattleClears`, `expectedBattleClearsSource`, `recordedAt`); a team with no depth has an empty `runDepths`. Ids only: no unit, objective or event display strings are served.

#### Scenario: Team ordering in the response

- **GIVEN** teams on gamma (order 0), alpha (orders 1, 0) and beta (order 0)
- **WHEN** the plan is read
- **THEN** `teams` lists alpha order 0, alpha order 1, beta order 0, gamma order 0

#### Scenario: Members keep their position order

- **GIVEN** a team created with `memberUnitIds` `[u3, u1, u2]`
- **WHEN** the plan is read
- **THEN** that team's `memberUnitIds` is `[u3, u1, u2]`
