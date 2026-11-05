## ADDED Requirements

### Requirement: An automatic snapshot is taken after a sync when none is recent

After every successful player-data sync, and after Legendary Event run records have been opened or refreshed by that sync, the system SHALL store a personal snapshot with `source` `Auto` of the just-synced roster when no roster snapshot of the profile has `takenAt` within the last 30 days (configurable). Personal snapshots of every source and Legendary Event run-record rosters SHALL count as snapshots. It SHALL NOT make any additional upstream call. The sync performed by `POST me/roster-snapshots` SHALL NOT take an automatic snapshot. A failure while taking the automatic snapshot SHALL NOT fail the sync. There is no per-user setting; the `RosterSnapshots` feature flag turns automatic snapshots off for everyone.

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

#### Scenario: Feature flag off

- **GIVEN** the `RosterSnapshots` feature flag is off and the profile has no snapshots
- **WHEN** a sync succeeds
- **THEN** no `Auto` snapshot is taken

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
