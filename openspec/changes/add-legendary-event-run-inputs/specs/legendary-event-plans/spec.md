# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

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
