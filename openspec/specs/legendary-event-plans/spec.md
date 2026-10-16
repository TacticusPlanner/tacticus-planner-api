# legendary-event-plans Specification

## Purpose

Defines the persisted Legendary Event plan per profile and event: its teams (lane, members, reserve, covered objectives, clear depth per run, order), the plan-level fields, how every write is validated against the Game Catalog, the single-revision concurrency contract, and the served shape every plan endpoint returns.

## Requirements

### Requirement: One plan per profile and Legendary Event

The system SHALL persist at most one plan per profile and catalog Legendary Event id. A plan SHALL carry a monotonic `revision`, the catalog version it was last written under (`catalogVersion`), optional `notes` (at most 2000 characters) and a `showPaidOptions` flag (default false). Plans SHALL be scoped to the caller's profile: a plan is never readable or writable by another profile, and deleting the account removes its plans and everything beneath them. A stored plan SHALL stay readable after its event leaves the current catalog; only writes require a current catalog event.

#### Scenario: A missing plan reads as an empty plan

- **GIVEN** a profile with no plan for event `astarLysander`
- **WHEN** the caller requests `GET /me/legendary-event-plans/astarLysander`
- **THEN** the response is 200 with `eventId` `astarLysander`, `revision` 0, `notes` null, `showPaidOptions` false, an empty `teams` list and the current `catalogVersion`, and no row is created

#### Scenario: Unknown event id

- **GIVEN** the profile has no stored plan for `notAnEvent`, which is not a current catalog `lres` id
- **WHEN** the caller requests or writes `/me/legendary-event-plans/notAnEvent`
- **THEN** the response is 404

#### Scenario: A stored plan outlives its catalog event

- **GIVEN** the profile has a plan at revision 4 for event `x`, and a later catalog no longer serves `x`
- **WHEN** the caller requests `GET /me/legendary-event-plans/x`
- **THEN** the response is 200 with the stored plan at revision 4 and its stored `catalogVersion`

#### Scenario: Writes to a plan outside the catalog are rejected

- **GIVEN** the same plan for `x`, no longer in the catalog
- **WHEN** the caller sends any `PUT`, `POST` or `DELETE` under `/me/legendary-event-plans/x`
- **THEN** the response is 404 and the plan is unchanged

#### Scenario: Plan-level write creates the plan

- **GIVEN** no plan exists for the event
- **WHEN** the caller sends `PUT /me/legendary-event-plans/{eventId}` with `expectedRevision` 0, `notes` "Alpha first" and `showPaidOptions` true
- **THEN** the plan is created, the response is 200 with `revision` 1, the given fields and the current `catalogVersion`

#### Scenario: Account purge cascades

- **GIVEN** a profile with a plan that has teams, members and objectives
- **WHEN** the account is deleted
- **THEN** the plan and all of its teams, members and objectives are removed

### Requirement: Teams belong to one lane of the plan

A team SHALL belong to exactly one lane (`alpha`, `beta` or `gamma`) of its plan and SHALL carry a trimmed `name` of 1–60 characters, an ordered list of 3–5 member unit ids, at most one reserve unit id, a set of covered objective indexes, at most one clear depth per run 1–3 (`expectedBattleClears` with its `expectedBattleClearsSource`, `estimate` or `manual`, and the time it was last written), and a `sortOrder` that is dense and 0-based within its lane. No unit SHALL appear twice in one team (members and reserve together). A team MAY cover zero objectives. A team stored with fewer than three members (written before this rule) SHALL stay readable, deletable and in its lane order, but any create or update that leaves a team below three members SHALL be rejected.

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

#### Scenario: A short team written earlier stays readable

- **GIVEN** a stored alpha team with members `[u1, u2]` written before the minimum existed
- **WHEN** the plan is read, the team is reordered within alpha, or the team is deleted
- **THEN** each call succeeds and the team is returned, kept in the new order, or removed

#### Scenario: Updating a short team without completing it

- **GIVEN** the same stored team with members `[u1, u2]`
- **WHEN** the caller sends `PUT …/teams/{teamId}` with members `[u1, u2]` and a new name
- **THEN** the response is 400 naming `memberUnitIds` and the team is unchanged

#### Scenario: Completing a short team

- **GIVEN** the same stored team
- **WHEN** the caller sends `PUT …/teams/{teamId}` with members `[u1, u2, u3]`
- **THEN** the response is 200 and the team has three members

### Requirement: Every team write is validated against the current Game Catalog

On create and update the system SHALL reject, with 400 and the offending field name, a `laneId` outside `alpha|beta|gamma`; a team with fewer than three or more than five members (the reserve does not count); a member or reserve unit id not present in that lane's `availableUnitIds`; a repeated unit; an `objectiveIndexes` entry that is not an `index` of that lane's `unitsRestrictions`, or a repeated index; a `run` outside 1..3; an `expectedBattleClears` outside 1..(battle count of the lane); a non-null depth without a source, or a source without a depth; an empty or over-long name. The plan's `catalogVersion` SHALL be set to the current catalog version on every successful write.

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

#### Scenario: Team below the minimum

- **WHEN** a team is created on alpha with `memberUnitIds` `[u1, u2]`
- **THEN** the response is 400 naming `memberUnitIds` and no team is created

#### Scenario: Reserve does not count toward the minimum

- **WHEN** a team is created with `memberUnitIds` `[u1, u2]` and `reserveUnitId` `u3`
- **THEN** the response is 400 naming `memberUnitIds`

#### Scenario: Team above the maximum

- **WHEN** a team is created with six members
- **THEN** the response is 400 naming `memberUnitIds`

#### Scenario: Exactly three and exactly five members are accepted

- **WHEN** teams are created with three members and with five members
- **THEN** both responses are 200

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

Every mutation (`PUT` plan, `POST`/`PUT`/`DELETE` team, `PUT` order, `PUT` run inputs, `PUT` annotations) SHALL carry `expectedRevision`. When it differs from the plan's current revision (0 for a plan that does not exist yet), the system SHALL make no change and respond 409 with `LegendaryEventPlanConflictResponse { issueCode: "legendaryEventPlanStale", message, plan }` carrying the current plan. A concurrent write detected at save time, including a unique-key violation when two callers create the missing plan at once, SHALL produce the same response after re-reading the plan. Every successful mutation SHALL bump the plan revision by exactly one and respond with the whole plan, with three exceptions that succeed without bumping the revision: a `PUT …/teams/order` whose order is already current (see "Lane order is replaced as a whole set"), and a `PUT …/runs/{run}` whose values equal the stored run inputs, or are all zero/false for a run with no stored inputs (see "Run inputs are stored per run"), and a `PUT …/annotations` that changes no cell (see "Maybe clear and Stop here annotations are stored per objective cell").

#### Scenario: Stale revision

- **GIVEN** a plan at revision 3
- **WHEN** the caller creates a team with `expectedRevision` 2
- **THEN** the response is 409 with `issueCode` `legendaryEventPlanStale` and the plan at revision 3, and no team is created

#### Scenario: Stale revision on a run write

- **GIVEN** a plan at revision 3
- **WHEN** the caller writes run 1 inputs with `expectedRevision` 2
- **THEN** the response is 409 with `issueCode` `legendaryEventPlanStale` and the plan at revision 3, and no run input changes

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

Every plan endpoint SHALL respond with the same `LegendaryEventPlanResponse`: `eventId`, `revision`, `catalogVersion`, `notes`, `showPaidOptions`, `teams` ordered by lane (`alpha`, `beta`, `gamma`) then `sortOrder`, `runs` ascending by run with one entry per stored run, and `annotations` ordered by lane (`alpha`, `beta`, `gamma`), then `battleIndex`, then `objectiveId`. Each team SHALL carry `id`, `laneId`, `name`, `sortOrder`, `memberUnitIds` in position order, `reserveUnitId` (null when none), `objectiveIndexes` ascending, and `runDepths` ascending by run with one entry per stored run (`run`, `expectedBattleClears`, `expectedBattleClearsSource`, `recordedAt`); a team with no depth has an empty `runDepths`. Each `runs` entry SHALL carry `run`, `regularMissions`, `premiumMissions`, `bundlePurchased`, `closeShards` and `updatedAt`; a plan with no stored run inputs has an empty `runs`. Each `annotations` entry SHALL carry `laneId`, `battleIndex`, `objectiveId`, `status` and `updatedAt`; a plan with none has an empty `annotations`. Ids only: no unit, objective or event display strings are served.

#### Scenario: Team ordering in the response

- **GIVEN** teams on gamma (order 0), alpha (orders 1, 0) and beta (order 0)
- **WHEN** the plan is read
- **THEN** `teams` lists alpha order 0, alpha order 1, beta order 0, gamma order 0

#### Scenario: Members keep their position order

- **GIVEN** a team created with `memberUnitIds` `[u3, u1, u2]`
- **WHEN** the plan is read
- **THEN** that team's `memberUnitIds` is `[u3, u1, u2]`

#### Scenario: Runs in the response

- **GIVEN** run inputs stored for runs 3 and 1
- **WHEN** the plan is read
- **THEN** `runs` lists run 1 then run 3, and run 2 is absent

#### Scenario: Empty plan has no runs

- **WHEN** a missing plan is read
- **THEN** `runs` and `annotations` are empty lists

#### Scenario: Annotations in the response

- **GIVEN** annotations (lane/`battleIndex`/`objectiveId`) on beta/2/1, alpha/9/0 and alpha/3/5
- **WHEN** the plan is read
- **THEN** `annotations` lists alpha/3/5, alpha/9/0, beta/2/1

### Requirement: Run inputs are stored per run

A plan SHALL store, per run 1–3, at most one set of run inputs: `regularMissions`, `premiumMissions`, `bundlePurchased` and `closeShards`, with the time it was last written (`updatedAt`). `PUT /me/legendary-event-plans/{eventId}/runs/{run}` with `{ expectedRevision, regularMissions, premiumMissions, bundlePurchased, closeShards }` SHALL create or replace that run's inputs and SHALL leave the other runs untouched. The system SHALL reject with 400, naming the field, a `run` outside 1..3, a `regularMissions` outside 0..(the event's catalog `regularMissions` count), a `premiumMissions` outside 0..(the event's catalog `premiumMissions` count), and a `closeShards` outside 0..75. Paid inputs SHALL be stored regardless of `showPaidOptions`. Deleting the plan or the account SHALL remove its run inputs.

Assumptions:

- Runs share one mission list per event; the catalog's `regularMissions` and `premiumMissions` arrays are that list (10 entries each for every event today).
- "Oh So Close" shards are awarded in chest-sized amounts of at most three per run (V1's 0–75 bound).

#### Scenario: Write run inputs on a new plan

- **GIVEN** no plan exists for `votanUthar`
- **WHEN** the caller sends `PUT /me/legendary-event-plans/votanUthar/runs/2` with `expectedRevision` 0, `regularMissions` 6, `premiumMissions` 0, `bundlePurchased` false, `closeShards` 0
- **THEN** the plan is created, the response is 200 with `revision` 1 and `runs` `[{run 2, 6, 0, false, 0, updatedAt}]`

#### Scenario: A run write touches only its run

- **GIVEN** a plan at revision 4 with run inputs for run 1 (`regularMissions` 10)
- **WHEN** the caller writes run 3 with `regularMissions` 10 and `bundlePurchased` true
- **THEN** the response has revision 5 and `runs` lists run 1 unchanged and run 3 with the new values

#### Scenario: Missions above the catalog count

- **GIVEN** `votanUthar` has 10 regular missions in the catalog
- **WHEN** the caller writes `regularMissions` 11
- **THEN** the response is 400 naming `regularMissions` and nothing changes

#### Scenario: Run outside the event

- **WHEN** the caller writes `/runs/4`
- **THEN** the response is 400 naming `run`

#### Scenario: Unchanged values keep the revision

- **GIVEN** run 1 is stored as `10, 0, false, 0` at plan revision 6
- **WHEN** the caller writes run 1 with the same values and `expectedRevision` 6
- **THEN** the response is 200 with the plan at revision 6

### Requirement: Maybe clear and Stop here annotations are stored per objective cell

A plan SHALL store, per lane, battle and objective cell, at most one annotation with status `maybeClear` or `stopHere` and the time it was last written (`updatedAt`). A cell SHALL be identified as the synced progress identifies it: `laneId`, `battleIndex` (0-based) and `objectiveId` (0 for defeat-all, 1–5 for the lane objective whose catalog `index` is `objectiveId − 1`). `PUT /me/legendary-event-plans/{eventId}/annotations` with `{ expectedRevision, laneId, battleIndex, objectiveIds, status }` SHALL set `status` on each listed cell of that battle, or remove their annotations when `status` is null, and SHALL leave every other cell untouched. The system SHALL reject with 400, naming the field, a `laneId` outside `alpha|beta|gamma`, a `battleIndex` outside 0..(lane battle count − 1), an empty `objectiveIds`, more than six or a repeated entry, an entry outside 0..(the lane's objective count), and a `status` other than `maybeClear`, `stopHere` or null. Annotations SHALL be stored whatever the synced progress says; whether one applies is decided by the client (an annotation applies only to a cell the synced progress shows not cleared). A sync SHALL never create, change or remove annotations. Deleting the plan or the account SHALL remove its annotations.

#### Scenario: Mark one cell on a new plan

- **GIVEN** no plan exists for `votanUthar`
- **WHEN** the caller sends `PUT …/votanUthar/annotations` with `expectedRevision` 0, `laneId` `alpha`, `battleIndex` 6, `objectiveIds` `[3]`, `status` `stopHere`
- **THEN** the plan is created at revision 1 and `annotations` is `[{alpha, 6, 3, stopHere, updatedAt}]`

#### Scenario: Mark a whole battle, then clear one cell

- **GIVEN** a plan at revision 2 with no annotations
- **WHEN** the caller marks alpha `battleIndex` 7, `objectiveIds` `[0, 1, 2, 4]`, `maybeClear`, then sends `objectiveIds` `[2]` with `status` null at revision 3
- **THEN** the plan is at revision 4 and alpha `battleIndex` 7 has annotations on objective ids 0, 1 and 4 only

#### Scenario: Objective outside the lane

- **WHEN** the caller sends `objectiveIds` `[6]`
- **THEN** the response is 400 naming `objectiveIds` and nothing changes

#### Scenario: Battle outside the lane

- **GIVEN** the alpha lane has 18 battles
- **WHEN** the caller sends `battleIndex` 18
- **THEN** the response is 400 naming `battleIndex`

#### Scenario: Unchanged annotation keeps the revision

- **GIVEN** alpha `battleIndex` 6, `objectiveId` 3 is `stopHere` at plan revision 5
- **WHEN** the caller sets the same cell to `stopHere` with `expectedRevision` 5, or clears a cell that has no annotation
- **THEN** the response is 200 with the plan at revision 5

### Requirement: Every stored plan is listed in one read

`GET /me/legendary-event-plans` SHALL return `LegendaryEventPlanListResponse { plans: LegendaryEventPlanResponse[] }` with every plan stored for the caller's profile, in the served plan shape, ordered by `eventId` (ordinal). Only stored plans SHALL be listed: a catalog event the caller never wrote a plan for SHALL NOT appear as an empty plan. Plans whose event is no longer in the catalog SHALL be listed with their stored `catalogVersion`. The read SHALL create nothing and SHALL NOT change any revision.

#### Scenario: Two stored plans

- **GIVEN** the caller has plans for `votanUthar` (revision 6, two Alpha teams) and `astarLysander` (revision 2, no teams, notes "Alpha first")
- **WHEN** the caller requests `GET /me/legendary-event-plans`
- **THEN** the response is 200 with `plans` `[astarLysander, votanUthar]`, each exactly as its single `GET` returns it

#### Scenario: No plans

- **GIVEN** the caller has never written a plan
- **WHEN** the list is requested
- **THEN** the response is 200 with `plans` `[]`

#### Scenario: Another profile's plans are not listed

- **GIVEN** profiles A and B each have a plan for `votanUthar`
- **WHEN** A requests the list
- **THEN** only A's plan is returned
