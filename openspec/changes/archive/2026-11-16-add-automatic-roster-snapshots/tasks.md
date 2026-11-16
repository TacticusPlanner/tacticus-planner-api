# Tasks

## 1. Settings

- [x] 1.1 No per-user setting: automatic snapshots are always on.

## 2. Automatic snapshots

- [x] 2.1 `AutoRosterSnapshotPolicy`: the 30-day check across personal snapshots and run-record rosters, capture with `RosterSnapshotFactory`, and the rolling quota of 12. Window and quota are configurable.
- [x] 2.2 Invoke it from `PlayerDataSyncService` after the run-record recorder, in the same transaction. Skip it for the sync inside `POST me/roster-snapshots`. Log failures without failing the sync.
- [x] 2.3 `POST me/roster-snapshots/{id}/keep`: Auto → Manual, refused at the manual limit; `rosterSnapshotNotAutomatic` otherwise.
- [x] 2.4 Add `autoLimit` and `autoCount` to the list response.

## 3. Tests

- [x] 3.1 Integration tests: first sync takes a baseline; a sync within 30 days of any snapshot (manual, auto, run record) takes none; a sync after 31 days takes one; a 13th auto replaces the oldest auto and never a manual one; Keep success, Keep at the limit, Keep on a manual snapshot; the snapshot-create sync does not also take an auto snapshot; an auto-step failure leaves the sync successful.

## 4. Contract and gates

- [x] 4.1 Regenerate `artifacts/openapi` and verify.
- [x] 4.2 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.
- [ ] 4.3 Coordinate the companion `tacticus-planner-apps` change.
