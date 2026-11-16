# personal-roster-snapshots Specification

## Purpose
TBD - created by archiving change add-automatic-roster-snapshots. Update Purpose after archive.

## Requirements

### Requirement: An automatic snapshot is taken after a sync when none is recent

After every successful player-data sync, and after Legendary Event run records have been opened or refreshed by that sync, the system SHALL store a personal snapshot with `source` `Auto` of the just-synced roster when no roster snapshot of the profile has `takenAt` within the last 30 days (configurable). Personal snapshots of every source and Legendary Event run-record rosters SHALL count as snapshots. It SHALL NOT make any additional upstream call. The sync performed by `POST me/roster-snapshots` SHALL NOT take an automatic snapshot. A failure while taking the automatic snapshot SHALL NOT fail the sync. There is no per-user setting.

#### Scenario: First sync takes a baseline

- **GIVEN** a profile with no snapshots and no run records
- **WHEN** its first sync succeeds
- **THEN** one `Auto` snapshot of the synced roster exists

#### Scenario: A recent manual snapshot suppresses it

- **GIVEN** a manual snapshot taken 12 days ago
- **WHEN** a sync succeeds
- **THEN** no `Auto` snapshot is taken

#### Scenario: A recent run record counts

- **GIVEN** the newest personal snapshot is 40 days old and a run record's roster was refreshed 5 days ago
- **WHEN** a sync succeeds
- **THEN** no `Auto` snapshot is taken

#### Scenario: A run record opened by this sync counts

- **GIVEN** the newest snapshot of any kind is 40 days old
- **WHEN** a sync succeeds and opens a new run record
- **THEN** no `Auto` snapshot is taken

#### Scenario: Thirty days without a snapshot

- **GIVEN** the newest snapshot of any kind was taken 31 days ago
- **WHEN** a sync succeeds
- **THEN** one `Auto` snapshot is taken with `takenAt` equal to the sync time

### Requirement: Automatic snapshots have their own rolling quota

A profile SHALL hold at most 12 `Auto` snapshots (configurable). Storing an automatic snapshot at the quota SHALL delete the oldest `Auto` snapshot by `takenAt` in the same transaction. `Auto` snapshots SHALL NOT count toward the manual limit, and the quota SHALL NOT delete `Manual` or `V1Import` snapshots. The list response SHALL carry `autoLimit` and `autoCount` alongside `limit` and `count`.

#### Scenario: Thirteenth automatic snapshot

- **GIVEN** 12 `Auto` snapshots, the oldest taken 2025-11-01, and 20 manual snapshots
- **WHEN** an automatic snapshot is taken
- **THEN** the 2025-11-01 snapshot is deleted, there are still 12 `Auto` and 20 manual snapshots, and `autoCount` is 12

### Requirement: An automatic snapshot can be kept as a manual one

`POST /api/v1/me/roster-snapshots/{id}/keep` SHALL change an `Auto` snapshot's `source` to `Manual`, keeping its name, `takenAt` and roster. It SHALL then count toward the manual limit. At the manual limit, the request SHALL be rejected with `rosterSnapshotLimitReached` and nothing changes. A snapshot whose source is not `Auto` SHALL be rejected with conflict `rosterSnapshotNotAutomatic`. An unknown or foreign id SHALL return 404.

#### Scenario: Keep

- **GIVEN** 5 manual snapshots and an `Auto` snapshot from 2026-10-01
- **WHEN** the profile keeps it
- **THEN** it is `Manual`, `count` is 6, and the next automatic quota replacement can no longer delete it

#### Scenario: Keep at the limit

- **GIVEN** 20 manual snapshots
- **WHEN** the profile keeps an `Auto` snapshot
- **THEN** the request is rejected with `rosterSnapshotLimitReached` and the snapshot stays `Auto`

### Requirement: A profile can take a personal roster snapshot from a fresh sync

`POST /api/v1/me/roster-snapshots` SHALL, for the authenticated profile, run a player-data sync with the Tacticus API and, in the same transaction that writes the synced data, store a new personal snapshot whose roster is a complete copy of the freshly synced characters and MoWs (see `roster-snapshots`), including when the sync changed no chunk, with `source` `Manual`, `takenAt` equal to the sync time, and the optional `name`. When the sync fails (upstream unavailable, timeout, missing or invalid key, rate limited), the request SHALL fail with that error and SHALL NOT create a snapshot. With `fromLastSync=true`, the request SHALL NOT call upstream and SHALL copy the stored synced roster, with `takenAt` equal to that snapshot's `syncedAt`. When the profile has no synced player data, it SHALL be rejected with `playerDataNotSynced`. The response SHALL be the created snapshot's metadata (see "Personal snapshots are listed as metadata").

#### Scenario: Snapshot from a fresh sync

- **GIVEN** a profile with a valid Tacticus key whose last sync was 2 hours ago, and the game now has `ultraCalgar` at Diamond3
- **WHEN** it posts `{ "name": "Before LE" }`
- **THEN** a sync runs, a snapshot named "Before LE" with source `Manual` is stored, and its roster has Calgar at Diamond3

#### Scenario: Repeat sync with unchanged roster still captures every unit

- **GIVEN** a profile whose stored player data already matches the game, with 92 characters and 14 MoWs
- **WHEN** it posts a snapshot
- **THEN** the snapshot's roster lists all 92 characters and 14 MoWs

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

A profile SHALL hold at most 20 personal snapshots whose `source` is `Manual` or `V1Import` (configurable, default 20). A create that would exceed the limit SHALL be rejected with conflict `rosterSnapshotLimitReached`, carrying `limit` and `count`. The limit SHALL be checked before any upstream call, so a rejected create SHALL NOT sync or change player data or run records. Two concurrent creates SHALL NOT together exceed the limit. A rejected create SHALL NOT replace or delete any snapshot. Snapshots with other sources and Legendary Event run-record rosters SHALL NOT count toward this limit.

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
