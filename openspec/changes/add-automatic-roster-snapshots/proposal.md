## Why

Most players won't remember to take snapshots, so the compare view has nothing to compare against when they want to see a month's progress. V1 had no automatic snapshots. An automatic snapshot taken when a profile has gone 30 days without any snapshot gives every active player a monthly baseline at no extra upstream cost.

Depends on `add-personal-roster-snapshots` (apply that first). Companion apps change: `add-automatic-roster-snapshots` in `tacticus-planner-apps` (auto labels, Keep action; it also carries the V1 import checkbox for `import-v1-roster-snapshots`). This API half applies first.

## What Changes

- After every successful player-data sync, the server takes an automatic snapshot (`source` `Auto`) of the just-synced roster when the profile has no roster snapshot with `takenAt` in the last 30 days. Manual, automatic and V1-imported personal snapshots and Legendary Event run-record rosters all count. No extra upstream call is made. A profile's first sync therefore takes a baseline.
- Automatic snapshots have their own rolling quota of 12. Taking a 13th deletes the oldest automatic one in the same transaction. Automatic snapshots never count toward the manual limit of 20, and the quota never touches manual or imported ones.
- `POST /api/v1/me/roster-snapshots/{id}/keep` converts an automatic snapshot into a manual one. It then counts toward the manual limit and is refused with `rosterSnapshotLimitReached` at 20.
- There is no per-user setting.
- Automatic snapshots appear in the list with `source` `Auto`. They can be read, renamed and deleted like manual ones.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `personal-roster-snapshots` (introduced by `add-personal-roster-snapshots`): automatic snapshots, their quota and Keep.

## Impact

- `Features/PlayerData/PlayerDataSyncService`: a post-sync hook that runs after the run-record recorder.
- `Features/RosterSnapshots`: the automatic-snapshot policy, the Keep endpoint and the list's `autoLimit`/`autoCount`.
- `artifacts/openapi`: the Keep operation and the new list fields.
