# Tasks

## 1. Import service

- [ ] 1.1 Add `RosterSnapshots` (default `false`) to `ImportV1Selection`, with part status and codes `not_selected`, `missing_roster_snapshots` and `invalid_roster_snapshots`.
- [ ] 1.2 Port V1 `resolveSnapshotDiff` and `fixSnapshot` to C#, with fixtures taken from V1 `roster-snapshots-service` behavior.
- [ ] 1.3 Map units: progression, rank, abilities, equipment slots, dropped locked or unknown units, `xp`/`appliedUpgradeSlots` unknown.
- [ ] 1.4 Order newest first, skip trashed snapshots and duplicates, apply room under the manual limit, and create `V1Import` snapshots under the profile lock.
- [ ] 1.5 Add `rosterSnapshotOutcomes` to the response, one per source snapshot: `{ name, takenAt, status, code, droppedUnits }`.

## 2. Tests

- [ ] 2.1 Chain reconstruction: base only; base + 3 diffs; a trashed middle entry still feeds later diffs; legacy invalid stars are fixed.
- [ ] 2.2 Mapping: a worked Calgar example; locked character and MoW dropped; unknown unit and item dropped and counted.
- [ ] 2.3 Limits and idempotency: 18 existing manual + 5 live V1 → 2 imported, 3 `skippedLimit`; re-run → all `skippedDuplicate`.

## 3. Contract and gates

- [ ] 3.1 Regenerate `artifacts/openapi` and verify.
- [ ] 3.2 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.
- [ ] 3.3 Coordinate the companion apps change (`add-automatic-roster-snapshots`).
