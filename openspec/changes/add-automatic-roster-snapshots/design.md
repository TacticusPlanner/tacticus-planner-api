## Context

`add-personal-roster-snapshots` adds the `personal_roster_snapshots` table with a `source` column (`Manual`, `Auto`, `V1Import`) and a manual limit of 20. LE run records hold a roster whose `takenAt` is rewritten when the record refreshes. `PlayerDataSyncService` already runs `LegendaryEventRunRecordRecorder` after each successful sync.

Companion: `add-automatic-roster-snapshots` in apps. Shared contract: `source` values, `POST me/roster-snapshots/{id}/keep`, the list's `autoLimit`/`autoCount`, and the `autoRosterSnapshots` user setting.

## Decisions

1. **Trigger: after every successful sync, in the same transaction, after the run-record recorder.** A run record opened or refreshed by that sync counts as a recent snapshot, so no automatic snapshot is added on top of it. The sync inside `POST me/roster-snapshots` skips the check, because a manual snapshot is about to be taken.
2. **30-day rule.** Take one when `max(takenAt)` over personal snapshots (any source) and run-record rosters is older than `now - 30 days`, or when none exist. The window is configuration with a default of 30 days.
3. **Rolling quota of 12 (configurable).** At the quota, the oldest `Auto` row (by `takenAt`) is deleted in the same transaction as the insert. Replacing is acceptable here, unlike for manual snapshots, because the system made these rows and the user can Keep any that matter.
4. **Keep** sets `source = Manual` and leaves the name and roster unchanged. It is refused at the manual limit, and for a snapshot that isn't `Auto` it returns 409 `rosterSnapshotNotAutomatic`.
5. **Name.** Automatic snapshots get the default ISO-date name. The client renders "Auto · Nov 3, 2026" from `source` and `takenAt`, so no display string is stored.
6. **Setting.** `autoRosterSnapshots` is a bool on user settings, default `true`. It is optional on update (absent means unchanged), so the existing required-fields contract does not break older clients.
7. **Failure isolation.** A failure in the automatic-snapshot step is logged and does not fail the sync.

## Risks / Trade-offs

- A player who syncs rarely gets fewer than one snapshot a month. That is acceptable, since their roster changed little without syncs.
- Storage per profile is bounded at 20 manual + 12 auto rosters.

## Migration Plan

`dotnet ef migrations add AddAutoRosterSnapshotsSetting`: adds the settings column with default `true`. No backfill: existing profiles get their first automatic snapshot on their next sync.
