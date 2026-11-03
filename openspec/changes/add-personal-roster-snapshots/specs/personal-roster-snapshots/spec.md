## ADDED Requirements

### Requirement: A profile can take a personal roster snapshot from a fresh sync

`POST /api/v1/me/roster-snapshots` SHALL, for the authenticated profile and under its profile lock, run a player-data sync with the Tacticus API and then store a new personal snapshot whose roster is a copy of the freshly synced characters and MoWs (see `roster-snapshots`), with `source` `Manual`, `takenAt` equal to the sync time, and the optional `name`. When the sync fails (upstream unavailable, timeout, missing or invalid key, rate limited), the request SHALL fail with that error and SHALL NOT create a snapshot. With `fromLastSync=true`, the request SHALL NOT call upstream and SHALL copy the stored synced roster, with `takenAt` equal to that snapshot's `syncedAt`. When the profile has no synced player data, it SHALL be rejected with `playerDataNotSynced`. The response SHALL be the created snapshot's metadata (see "Personal snapshots are listed as metadata").

#### Scenario: Snapshot from a fresh sync

- **GIVEN** a profile with a valid Tacticus key whose last sync was 2 hours ago, and the game now has `ultraCalgar` at Diamond3
- **WHEN** it posts `{ "name": "Before LE" }`
- **THEN** a sync runs, a snapshot named "Before LE" with source `Manual` is stored, and its roster has Calgar at Diamond3

#### Scenario: Sync failure creates nothing

- **GIVEN** the Tacticus API is unavailable
- **WHEN** the profile posts a snapshot
- **THEN** the response is the sync's 503 error and the profile's snapshot count is unchanged

#### Scenario: Explicit fallback to the last sync

- **GIVEN** the profile's last sync was at 2026-11-03T10:00Z and the Tacticus API is unavailable
- **WHEN** it posts with `fromLastSync=true`
- **THEN** no upstream call is made and a snapshot with `takenAt` 2026-11-03T10:00Z is stored

#### Scenario: Never synced

- **GIVEN** a profile with no synced player data
- **WHEN** it posts with `fromLastSync=true`
- **THEN** the request is rejected with `playerDataNotSynced`

### Requirement: Personal snapshots are limited per profile

A profile SHALL hold at most 20 personal snapshots whose `source` is `Manual` or `V1Import` (configurable, default 20). A create that would exceed the limit SHALL be rejected with conflict `rosterSnapshotLimitReached`, carrying `limit` and `count`. It SHALL NOT sync, and it SHALL NOT replace or delete any snapshot. Snapshots with other sources and Legendary Event run-record rosters SHALL NOT count toward this limit.

#### Scenario: Limit reached

- **GIVEN** a profile with 20 manual snapshots
- **WHEN** it posts a new snapshot
- **THEN** the request is rejected with `rosterSnapshotLimitReached`, `limit` 20, `count` 20, no upstream call is made and all 20 remain

#### Scenario: Run records do not count

- **GIVEN** a profile with 19 manual snapshots and 6 Legendary Event run records
- **WHEN** it posts a new snapshot
- **THEN** the snapshot is created and the count is 20

### Requirement: Snapshot names are optional and need not be unique

A snapshot `name` SHALL be trimmed and SHALL be 1 to 60 characters after trimming. An absent or blank name on create SHALL be stored as the ISO date of `takenAt` (`YYYY-MM-DD`, UTC). Names SHALL NOT be required to be unique. A name longer than 60 characters SHALL be rejected as a validation error. `PATCH /api/v1/me/roster-snapshots/{id}` SHALL rename a snapshot under the same rules and SHALL NOT change its roster or `takenAt`.

#### Scenario: Default name

- **WHEN** the profile posts a snapshot with no name at 2026-11-03T18:00Z
- **THEN** the snapshot is named `2026-11-03`

#### Scenario: Duplicate names allowed

- **GIVEN** a snapshot named "Weekly"
- **WHEN** the profile posts another snapshot named "Weekly"
- **THEN** both exist with different ids

#### Scenario: Rename keeps the roster

- **WHEN** the profile renames a snapshot to "  After Ragnar  "
- **THEN** its name is "After Ragnar" and its roster and `takenAt` are unchanged

### Requirement: Personal snapshots are listed as metadata

`GET /api/v1/me/roster-snapshots` SHALL return `{ limit, count, items }`, where `items` lists the profile's personal snapshots newest `takenAt` first, each `{ id, name, source, takenAt, characterCount, mowCount }`, without a roster. `count` SHALL be the number counted toward the limit. Legendary Event run-record rosters SHALL NOT be listed.

#### Scenario: List order and counts

- **GIVEN** manual snapshots taken on 2026-10-01 and 2026-11-01
- **WHEN** the profile lists its snapshots
- **THEN** the 2026-11-01 snapshot is first, `count` is 2 and `limit` is 20

### Requirement: A personal snapshot's roster is read by id

`GET /api/v1/me/roster-snapshots/{id}` SHALL return that snapshot's roster in the `RosterSnapshotResponse` shape. The roster SHALL never change after creation. An unknown id, or one owned by another profile, SHALL return 404.

#### Scenario: Another profile's snapshot

- **WHEN** profile B requests a snapshot id owned by profile A
- **THEN** the response is 404

### Requirement: Personal snapshots are deleted permanently

`DELETE /api/v1/me/roster-snapshots/{id}` SHALL permanently delete the snapshot. There SHALL be no trash or restore. An unknown or foreign id SHALL return 404. Deleting a snapshot SHALL NOT affect the synced roster or any run record.

#### Scenario: Delete frees a slot

- **GIVEN** a profile at the 20-snapshot limit
- **WHEN** it deletes one snapshot and then posts a new one
- **THEN** both requests succeed and the count is 20

### Requirement: Personal snapshots follow the account lifecycle

Account purge SHALL delete every personal snapshot of the profile. A staging transfer export SHALL include the profile's personal snapshots with their rosters, and an import SHALL recreate them with the same names, sources and `takenAt`.

#### Scenario: Purge

- **WHEN** an account with personal snapshots is purged
- **THEN** none of its snapshots remain
