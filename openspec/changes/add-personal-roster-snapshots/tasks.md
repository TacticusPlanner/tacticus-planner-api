# Tasks

## 1. Domain and persistence

- [x] 1.1 Add `PersonalRosterSnapshot` (`Id`, `ProfileId`, `Name`, `Source`, `CreatedAt`, `Roster`) and the `RosterSnapshotSource` enum (`Manual`, `Auto`, `V1Import`; only `Manual` written here).
- [x] 1.2 EF configuration with `OwnsOne(Roster).ToJson()`, an index on `(profile_id, taken_at desc, id desc)` (the list orders by `takenAt`, which V1 imports set to the past), and cascade delete from profiles.
- [x] 1.3 `dotnet ef migrations add AddPersonalRosterSnapshots`.
- [x] 1.4 Extract `RosterSnapshotFactory` from `LegendaryEventRunRecordRecorder` and switch the recorder to it, with no behavior change. Existing run-record tests stay green.

## 2. Endpoints

- [x] 2.1 Add an optional post-write callback to `PlayerDataSyncService` that receives the transformed `Characters`/`Mows` inside `WriteAsync`'s transaction, after `RecordRunsAsync`.
- [x] 2.2 `POST me/roster-snapshots`: check the limit first and return `rosterSnapshotLimitReached` without calling the sync service, so player data and run records are unchanged. Otherwise sync with the callback, which re-checks the limit and builds the snapshot from the transformed roster in the same transaction. Support `fromLastSync=true`, which loads the stored `characters`/`mows` chunks without calling upstream. Errors: `rosterSnapshotLimitReached`, `playerDataNotSynced`, and the upstream errors the sync already returns.
- [x] 2.3 `GET me/roster-snapshots`: metadata list, newest first, with `limit` and `count`.
- [x] 2.4 `GET me/roster-snapshots/{id}`: `RosterSnapshotResponse`. 404 for an unknown id or another profile's id.
- [x] 2.5 `PATCH me/roster-snapshots/{id}`: rename, with the same name rules.
- [x] 2.6 `DELETE me/roster-snapshots/{id}`: hard delete. 404 when it is unknown or not owned.
- [x] 2.7 Validators (name length, trimming) and the domain invariant check before save.
- [x] 2.8 Apply the `upstream-slow` limit to `POST me/roster-snapshots` only when `fromLastSync` is not `true`. `UpstreamRateLimitPreProcessor` reads endpoint metadata only, so add a request-level predicate to it (or split the fallback into its own route). The `fromLastSync=true` fallback stays unlimited, with a test that it succeeds after the slow quota is exhausted.

## 3. Lifecycle

- [x] 3.1 Account purge removes personal snapshots.
- [x] 3.2 Staging transfer exports and imports personal snapshots.
- [ ] 3.3 (Not landed yet; left for `add-debug-info-export`.) If `add-debug-info-export` has landed, include the snapshot metadata list (no rosters) in the debug-info export.

## 4. Tests

- [x] 4.1 Integration tests: create with sync, create with fallback, sync failure creates nothing, limit at 20, default name, rename, delete, list order and counts, ownership 404s, rate limit, purge, staging round-trip.

## 5. Contract and gates

- [x] 5.1 Regenerate `artifacts/openapi` and verify the new operations and schemas.
- [ ] 5.2 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.
- [ ] 5.3 Coordinate the companion `tacticus-planner-apps` change (pull the merged API SHA there).
