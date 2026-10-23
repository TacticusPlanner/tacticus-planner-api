## MODIFIED Requirements

### Requirement: A run record stores lanes, teams and a roster snapshot as of its last progress

A run record SHALL store: `run` (1–3); `startedAt` and `progressAt` (UTC); the `catalogVersion` and the synced event `points` at `progressAt`; `lanes` with `alpha`, `beta`, `gamma`, each null (as synced) or the synced encounters in order as `{ objectivesCleared: int[], highScore: int }` (synced `encounterPoints` SHALL NOT be stored); `teams`, the profile's plan teams for the event at `progressAt` in lane then `sortOrder` order, each `{ teamId, laneId, name, sortOrder, memberUnitIds, reserveUnitId, objectiveIndexes, expectedBattleClears, expectedBattleClearsSource }` with the depth being that team's stored depth for the record's `run` or null (an empty list when there is no plan or no team); and its own inline roster snapshot (see `roster-snapshots`), which is never null. Records SHALL be scoped to the caller's profile and SHALL be removed with the account. Records SHALL NOT be editable through the API.

#### Scenario: Teams snapshot carries the run's depth

- **GIVEN** a plan for `votanUthar` with Alpha team "Ultras" (members `ultraCalgar`, `ultraTigurius`, `ultraTitus`, objectives `[0,3]`, `runDepths` `[{run 1, 9, manual}, {run 2, 7, estimate}]`)
- **WHEN** a sync opens a run 2 record
- **THEN** its `teams` holds "Ultras" with those members and objectives, `expectedBattleClears` 7 and `expectedBattleClearsSource` `estimate`

#### Scenario: Each record has its own roster snapshot

- **GIVEN** a sync opens an Uthar run 2 record while an Uthar run 1 record exists
- **WHEN** the sync completes
- **THEN** the run 2 record carries a roster taken at this sync and the run 1 record's roster is unchanged

#### Scenario: Account purge removes records

- **WHEN** an account with run records is deleted
- **THEN** its records are removed

### Requirement: Run records are readable per event

`GET /me/legendary-event-plans/{eventId}/run-records?limit=&after=` SHALL return `{ eventId, records, nextAfter }`, where `records` lists the caller's records for that event ordered by `startedAt DESC, id DESC`, each `{ id, run, startedAt, progressAt, catalogVersion, points, lanes, teams }` (no roster), at most `limit` (default 20, maximum 100). `after` SHALL be an opaque, versioned base64url cursor encoding both the UTC `startedAt` and UUID `id` of the last record of the previous page; `nextAfter` SHALL encode the same pair or be null when no more records exist. The next-page predicate SHALL use the lexicographic less-than comparison on `(startedAt, id)`, matching that descending order. Malformed cursors SHALL return 400 naming `after`. A current catalog event with no records SHALL answer 200 with `records` `[]`. An id that is neither a current catalog `lres` id nor has any record for the caller SHALL answer 404. An event that left the catalog SHALL keep answering with its records.

#### Scenario: Records newest first

- **GIVEN** Uthar records for run 1 (started 2026-10-04) and run 2 (started 2026-10-11)
- **WHEN** the caller requests `GET /me/legendary-event-plans/votanUthar/run-records`
- **THEN** the response is 200 with the run 2 record first, then run 1, and `nextAfter` null

#### Scenario: Paging

- **GIVEN** 25 Uthar records
- **WHEN** the caller requests with `limit` 20 and then with `after` set to the returned `nextAfter`
- **THEN** the first response holds the 20 newest with a non-null `nextAfter`, the second holds the remaining 5 with `nextAfter` null, and no record appears twice

#### Scenario: Equal timestamps across a page boundary

- **GIVEN** more than one page of records share the same `startedAt` and have distinct ids
- **WHEN** the caller follows `nextAfter` until it is null
- **THEN** records are ordered by descending id within that timestamp and every record appears exactly once

#### Scenario: Invalid cursor

- **WHEN** the caller sends a malformed `after` cursor or an unsupported cursor version
- **THEN** the response is 400 naming `after`

#### Scenario: Invalid limit

- **WHEN** the caller requests `limit` 0 or 101
- **THEN** the response is 400 naming `limit`

#### Scenario: No records yet

- **WHEN** the caller requests run records for `astarLysander` with none stored
- **THEN** the response is 200 with `eventId` `astarLysander`, `records` `[]` and `nextAfter` null

#### Scenario: Unknown event

- **WHEN** the caller requests run records for `notAnEvent`, which has no records and is not in the catalog
- **THEN** the response is 404

## ADDED Requirements

### Requirement: A run record's roster is read on demand

`GET /me/legendary-event-plans/{eventId}/run-records/{recordId}/roster` SHALL return the record's roster snapshot as `{ takenAt, catalogVersion, characters, mows }` in the shapes defined by `roster-snapshots`. The response SHALL carry an `ETag` that changes whenever the roster is rewritten and SHALL answer 304 to a matching `If-None-Match`. A record not owned by the caller or not belonging to `eventId` SHALL answer 404.

#### Scenario: Roster of an own record

- **GIVEN** an Uthar run 2 record whose roster was taken at 2026-10-12T19:00Z
- **WHEN** the caller requests its roster
- **THEN** the response is 200 with `takenAt` 2026-10-12T19:00Z and the full `characters`/`mows` lists

#### Scenario: Unchanged roster

- **WHEN** the caller repeats the request with the previous `ETag` in `If-None-Match` and no sync rewrote the roster
- **THEN** the response is 304

#### Scenario: Foreign record

- **WHEN** the caller requests the roster of another profile's record
- **THEN** the response is 404
