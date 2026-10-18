## MODIFIED Requirements

### Requirement: Run inputs are stored per run

A plan SHALL store, per run 1–3, at most one set of run inputs: `regularMissions`, `premiumMissions`, `completedRegularMissions`, `completedPremiumMissions`, `bundlePurchased` and `closeShards`, with the time it was last written (`updatedAt`). `PUT /me/legendary-event-plans/{eventId}/runs/{run}` with `{ expectedRevision, regularMissions, premiumMissions, completedRegularMissions, completedPremiumMissions, bundlePurchased, closeShards }` SHALL create or replace that run's inputs and SHALL leave the other runs untouched; absent completed arrays mean empty. The system SHALL reject with 400, naming the field, a `run` outside 1..3, a `regularMissions` outside 0..(the event's catalog `regularMissions` count), a `premiumMissions` outside 0..(the event's catalog `premiumMissions` count), a completed array with a duplicate or an index outside its catalog list, a count below its completed array's length, and a `closeShards` outside 0..75. Completed arrays SHALL be stored sorted ascending. Paid inputs SHALL be stored regardless of `showPaidOptions`. Deleting the plan or the account SHALL remove its run inputs.

Assumptions:

- Runs share one mission list per event; the catalog's `regularMissions` and `premiumMissions` arrays are that list (10 entries each for every event today).
- "Oh So Close" shards are awarded in chest-sized amounts of at most three per run (V1's 0–75 bound).
- Counts are the missions a run will complete (used by forecasts); completed arrays are the ones already done.

#### Scenario: Write run inputs on a new plan

- **GIVEN** no plan exists for `votanUthar`
- **WHEN** the caller sends `PUT /me/legendary-event-plans/votanUthar/runs/2` with `expectedRevision` 0, `regularMissions` 6, `premiumMissions` 0, `completedRegularMissions` [3, 1], `bundlePurchased` false, `closeShards` 0
- **THEN** the plan is created, the response is 200 with `revision` 1 and `runs` `[{run 2, 6, 0, [1, 3], [], false, 0, updatedAt}]`

#### Scenario: A run write touches only its run

- **GIVEN** a plan at revision 4 with run inputs for run 1 (`regularMissions` 10)
- **WHEN** the caller writes run 3 with `regularMissions` 10 and `bundlePurchased` true
- **THEN** the response has revision 5 and `runs` lists run 1 unchanged and run 3 with the new values

#### Scenario: Missions above the catalog count

- **GIVEN** `votanUthar` has 10 regular missions in the catalog
- **WHEN** the caller writes `regularMissions` 11
- **THEN** the response is 400 naming `regularMissions` and nothing changes

#### Scenario: Count below ticks

- **WHEN** the caller writes `regularMissions` 1 with `completedRegularMissions` [0, 2]
- **THEN** the response is 400 naming `regularMissions` and nothing changes

#### Scenario: Index outside the list

- **WHEN** the caller writes `completedPremiumMissions` [10] on a 10-mission event
- **THEN** the response is 400 naming `completedPremiumMissions`

#### Scenario: Run outside the event

- **WHEN** the caller writes `/runs/4`
- **THEN** the response is 400 naming `run`

#### Scenario: Unchanged values keep the revision

- **GIVEN** run 1 is stored as `10, 0, [], [], false, 0` at plan revision 6
- **WHEN** the caller writes run 1 with the same values and `expectedRevision` 6
- **THEN** the response is 200 with the plan at revision 6
