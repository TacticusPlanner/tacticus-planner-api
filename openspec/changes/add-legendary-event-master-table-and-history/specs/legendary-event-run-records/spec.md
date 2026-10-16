# Spec Delta

## ADDED Requirements

### Requirement: A run record is opened or refreshed on every sync

On every successful player sync, the system SHALL examine each record of the incoming `lre-progress` chunk whose `id` is a current catalog `lres` id, whose `currentEventRun` is 1..3, and which has at least one encounter on some lane. For each, with `latest` the profile's newest run record for that event (by `startedAt`):

- it SHALL **open** a new record when `latest` is absent, when `latest.run` differs from the incoming run, or when the incoming lanes are not a superset of `latest.lanes` (for some lane and battle a stored cleared objective id is missing, or a stored battle is missing);
- otherwise it SHALL **refresh** `latest` when the incoming compact lanes differ from the stored lanes;
- otherwise it SHALL leave `latest` unchanged.

Opening sets `startedAt` and `progressAt` to the sync time; refreshing sets `progressAt` only. Both SHALL write the record's `lanes`, `points`, `catalogVersion`, `teams` and `roster` from the incoming snapshot and the profile's plan at that moment. The write SHALL commit in the same transaction as the snapshot, and a sync that fails or is retried SHALL leave exactly the records a single successful sync would have left. Events outside the catalog, entries with no run, and entries with no encounters SHALL be ignored.

Assumptions:

- The Tacticus API reports one progress entry per event, for its current or most recent run; a new run resets that entry's encounters.
- `currentEventRun` is the run (1–3) of the current occurrence; a later re-run of the event starts again at 1.
- A run whose whole play happens between two syncs is never seen and is not recorded.

#### Scenario: First sync during a run opens a record

- **GIVEN** no record for `votanUthar`, and a sync whose Uthar entry has `currentEventRun` 2 and Alpha `encounters` `[{ objectivesCleared [0,1,2], highScore 34 }]`
- **WHEN** the sync completes
- **THEN** one record exists with `run` 2, `startedAt` = `progressAt` = the sync time, Alpha lanes `[{ objectivesCleared [0,1,2], highScore 34 }]`, and Beta and Gamma null

#### Scenario: Progress within the run refreshes the record

- **GIVEN** that record, and a later sync whose Alpha entry adds battle 2 `{ objectivesCleared [0], highScore 30 }`
- **WHEN** the sync completes
- **THEN** the same record now has two Alpha encounters, a new `progressAt`, the unchanged `startedAt`, and the roster and teams as of this sync

#### Scenario: A sync with unchanged progress keeps the record frozen

- **GIVEN** the record above, and the player ranks up a team member and syncs, with Uthar's encounters unchanged
- **WHEN** the sync completes
- **THEN** the record's `progressAt`, `roster` and `teams` are unchanged

#### Scenario: The next run opens a new record

- **GIVEN** the record for run 2, and a sync whose Uthar entry has `currentEventRun` 3 and Alpha `encounters` `[{ objectivesCleared [0], highScore 20 }]`
- **WHEN** the sync completes
- **THEN** a second record with `run` 3 is opened and the run 2 record is unchanged

#### Scenario: A reset with the same run number opens a new record

- **GIVEN** the newest Uthar record has `run` 1 with nine Alpha encounters, and a sync months later reports `currentEventRun` 1 with two Alpha encounters
- **WHEN** the sync completes
- **THEN** a new record with `run` 1 is opened and the earlier one is kept

#### Scenario: An event the catalog does not serve is ignored

- **WHEN** a sync's `lre-progress` carries an entry for an id that is not a current catalog `lres` id
- **THEN** no record is opened or changed for it

#### Scenario: An entry with no encounters is ignored

- **GIVEN** a sync whose Lysander entry has `currentEventRun` 1 and every lane null or with no encounters
- **WHEN** the sync completes
- **THEN** no Lysander record is opened

### Requirement: A run record stores lanes, teams and roster as of its last progress

A run record SHALL store: `run` (1–3); `startedAt` and `progressAt` (UTC); the `catalogVersion` and the synced event `points` at `progressAt`; `lanes` with `alpha`, `beta`, `gamma`, each null (as synced) or the synced encounters in order as `{ objectivesCleared: int[], highScore: int }` (synced `encounterPoints` SHALL NOT be stored); `teams`, the profile's plan teams for the event at `progressAt` in lane then `sortOrder` order, each `{ teamId, laneId, name, sortOrder, memberUnitIds, reserveUnitId, objectiveIndexes, expectedBattleClears, expectedBattleClearsSource }` with the depth being that team's stored depth for the record's `run` or null (an empty list when there is no plan or no team); and `roster`, every owned character or MoW in the snapshot whose id is in the `availableUnitIds` of at least one lane of the event, ordered by `unitId`, each `{ unitId, progressionIndex, rank, xpLevel, appliedUpgradeSlots, abilities: [{ abilityId, level }] }` with `rank` null for a MoW. Records SHALL be scoped to the caller's profile and SHALL be removed with the account. Records SHALL NOT be editable through the API.

#### Scenario: Teams snapshot carries the run's depth

- **GIVEN** a plan for `votanUthar` with Alpha team "Ultras" (members `ultraCalgar`, `ultraTigurius`, `ultraTitus`, objectives `[0,3]`, `runDepths` `[{run 1, 9, manual}, {run 2, 7, estimate}]`)
- **WHEN** a sync opens a run 2 record
- **THEN** its `teams` holds "Ultras" with those members and objectives, `expectedBattleClears` 7 and `expectedBattleClearsSource` `estimate`

#### Scenario: Roster holds only owned units the event allows

- **GIVEN** the synced roster owns `ultraCalgar` (Diamond1, `Legendary:RedFiveStars`) and `tauFarsight`, Uthar's lanes allow `ultraCalgar` and not `tauFarsight`, and `ultraTigurius` is allowed but not owned
- **WHEN** a sync opens an Uthar record
- **THEN** `roster` contains `ultraCalgar` with rank `Diamond1` and progression `Legendary:RedFiveStars`, and contains neither `tauFarsight` nor `ultraTigurius`

#### Scenario: Account purge removes records

- **WHEN** an account with run records is deleted
- **THEN** its records are removed

### Requirement: Run records are readable per event

`GET /me/legendary-event-plans/{eventId}/run-records` SHALL return `LegendaryEventRunRecordsResponse { eventId, records }`, where `records` lists the caller's records for that event, newest `startedAt` first, each `{ id, run, startedAt, progressAt, catalogVersion, points, lanes, teams, roster }` in the stored shapes. A current catalog event with no records SHALL answer 200 with `records` `[]`. An id that is neither a current catalog `lres` id nor has any record for the caller SHALL answer 404. An event that left the catalog SHALL keep answering with its records.

#### Scenario: Records newest first

- **GIVEN** Uthar records for run 1 (started 2026-10-04) and run 2 (started 2026-10-11)
- **WHEN** the caller requests `GET /me/legendary-event-plans/votanUthar/run-records`
- **THEN** the response is 200 with the run 2 record first, then run 1

#### Scenario: No records yet

- **WHEN** the caller requests run records for `astarLysander` with none stored
- **THEN** the response is 200 with `eventId` `astarLysander` and `records` `[]`

#### Scenario: Unknown event

- **WHEN** the caller requests run records for `notAnEvent`, which has no records and is not in the catalog
- **THEN** the response is 404
