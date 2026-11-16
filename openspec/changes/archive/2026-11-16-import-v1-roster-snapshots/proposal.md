## Why

V1 players have up to 20 roster snapshots stored in their V1 user data (`rosterSnapshots`), often going back months. Without an import, their history is gone the moment they move to V2, which is the main reason to keep using V1's Roster Snapshots page.

Depends on `add-personal-roster-snapshots` (apply that first). Companion apps work ships in `add-automatic-roster-snapshots` in `tacticus-planner-apps`: the "Roster snapshots" checkbox and the outcome rendering on the V1 import page. This API half applies first.

## What Changes

- New `rosterSnapshots` part on `POST /api/v1/account/v1-import` (`ImportV1Selection.RosterSnapshots`, default `false`).
- The import reads V1 `rosterSnapshots` (`{ base, diffs[] }`) and rebuilds every snapshot from the base-plus-diff chain in memory, applying V1's `resolveSnapshotDiff` and `fixSnapshot` rules. Snapshots V1 had moved to trash (`deletedDateMillisUtc` set) are skipped.
- Each live V1 snapshot becomes a personal snapshot with `source` `V1Import`, `takenAt` from V1 `dateMillisUtc`, and the V1 name (trimmed and cut to 60 characters). The client labels it "V1 · <name>".
- Units are mapped onto catalog ids and the V2 roster shape. Locked units are dropped. Total XP and applied upgrade slots, which V1 did not record, are stored as `0` and `[]`, and the `V1Import` source tells the client to show them as unknown.
- Snapshots are imported newest first, up to the room left under the manual limit of 20. Every source snapshot gets one outcome: `imported`, `skippedLimit`, `skippedDuplicate`, `skippedDeleted` or `failed`.
- Re-running the import is idempotent: each imported snapshot stores an immutable import key (V1 date and original name), and a V1 snapshot whose key already exists is skipped, even when the user renamed it.

## Capabilities

### New Capabilities

- `v1-roster-snapshot-import`: the `rosterSnapshots` part. It covers chain reconstruction, unit mapping, limits, idempotency and per-snapshot outcomes.

### Modified Capabilities

- `roster-snapshots`: `V1Import` snapshots are exempt from the "copied as synced" and capture-time catalog-version rules.

## Impact

- `Features/V1Import`: `V1RosterSnapshotImportService` (new), the selection flag, and `rosterSnapshotOutcomes` on the response.
- Reuses `ProgressionWire`/`ProgressionRules` from the goal import, and `RosterSnapshotFactory` and the limit from `Features/RosterSnapshots`.
- `artifacts/openapi`: the selection flag and the outcome schema.
- Tests: chain reconstruction fixtures taken from real V1 blobs (base only, base + diffs, trashed entries, legacy bad stars), the mapping, the limit and idempotency.
- Migration `AddRosterSnapshotImportKey`: a nullable `import_key` column with a unique index on `(profile_id, import_key)`.
