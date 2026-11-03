# Tasks

## 1. Domain and persistence

- [ ] 1.1 Add `PersonalRosterSnapshot` (`Id`, `ProfileId`, `Name`, `Source`, `CreatedAt`, `Roster`) and the `RosterSnapshotSource` enum (`Manual`, `Auto`, `V1Import`; only `Manual` written here).
- [ ] 1.2 EF configuration with `OwnsOne(Roster).ToJson()`, an index on `(profile_id, created_at desc)`, and cascade delete from profiles.
- [ ] 1.3 `dotnet ef migrations add AddPersonalRosterSnapshots`.
- [ ] 1.4 Extract `RosterSnapshotFactory` from `LegendaryEventRunRecordRecorder` and switch the recorder to it, with no behavior change. Existing run-record tests stay green.

## 2. Endpoints

- [ ] 2.1 `POST me/roster-snapshots`: under the profile lock, sync (reusing the sync service), check the limit, capture, and save. Support `fromLastSync=true`. Errors: `rosterSnapshotLimitReached`, `playerDataNotSynced`, and the upstream errors the sync already returns.
- [ ] 2.2 `GET me/roster-snapshots`: metadata list, newest first, with `limit` and `count`.
- [ ] 2.3 `GET me/roster-snapshots/{id}`: `RosterSnapshotResponse`. 404 for an unknown id or another profile's id.
- [ ] 2.4 `PATCH me/roster-snapshots/{id}`: rename, with the same name rules.
- [ ] 2.5 `DELETE me/roster-snapshots/{id}`: hard delete. 404 when it is unknown or not owned.
- [ ] 2.6 Validators (name length, trimming) and the domain invariant check before save.
- [ ] 2.7 Add `POST me/roster-snapshots` (sync path) to the `upstream-slow` limiter.

## 3. Lifecycle

- [ ] 3.1 Account purge removes personal snapshots.
- [ ] 3.2 Staging transfer exports and imports personal snapshots.
- [ ] 3.3 If `add-debug-info-export` has landed, include the snapshot metadata list (no rosters) in the debug-info export.

## 4. Tests

- [ ] 4.1 Integration tests: create with sync, create with fallback, sync failure creates nothing, limit at 20, default name, rename, delete, list order and counts, ownership 404s, rate limit, purge, staging round-trip.

## 5. Contract and gates

- [ ] 5.1 Regenerate `artifacts/openapi` and verify the new operations and schemas.
- [ ] 5.2 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.
- [ ] 5.3 Coordinate the companion `tacticus-planner-apps` change (pull the merged API SHA there).
