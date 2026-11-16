## Why

V1's Roster Snapshots page (about 10k views from 5.4k users in the last 90 days) lets a player save named copies of their roster and compare them with each other or with the current roster. V2 has no equivalent. The `roster-snapshots` store already copies the whole owned roster, but only Legendary Event run records own one, so a player cannot keep a snapshot of their own.

This change adds personal, named roster snapshots: a server-side capture from a fresh sync, list/read/rename/delete, and a per-profile limit. Planning and decisions are in the project's `roster-snapshots/exploration.md`.

Companion apps change: `add-personal-roster-snapshots` in `tacticus-planner-apps` (Collection compare mode, take and manage flows, diff model). This API half applies first. Follow-up API changes: `add-automatic-roster-snapshots` and `import-v1-roster-snapshots`.

## What Changes

- New `personal_roster_snapshots` table: one row per snapshot with `id` (UUIDv7), `profileId`, `name`, `source` (`Manual` only in this change; `Auto` and `V1Import` are reserved for the follow-ups), `createdAt`, and the roster as a jsonb `RosterSnapshot` (ADR 0011), the same shape run records store.
- `POST /api/v1/me/roster-snapshots { name? }`: syncs the profile with the Tacticus API, then copies the freshly synced roster into a new snapshot in one request. When the sync fails, the request fails, and nothing is created. With `fromLastSync=true`, it captures the last synced roster without calling upstream, so the client can offer an explicit "Take from last sync" fallback.
- `GET /api/v1/me/roster-snapshots`: the profile's personal snapshots as metadata only (no roster), newest first, plus the limit and the count.
- `GET /api/v1/me/roster-snapshots/{id}`: one snapshot's roster in the existing `RosterSnapshotResponse` shape.
- `PATCH /api/v1/me/roster-snapshots/{id} { name }` renames a snapshot. `DELETE /api/v1/me/roster-snapshots/{id}` hard-deletes it, with no trash.
- At most 20 manual snapshots per profile. A create at the limit is refused with `rosterSnapshotLimitReached`, and nothing is replaced.
- Names are optional and default to the capture date. They are trimmed, can be up to 60 characters, and may repeat.
- The create endpoint calls upstream, so it joins the `upstream-slow` rate-limit group.
- Account purge and staging transfer export/import include personal snapshots, and so does the debug-info export once `add-debug-info-export` lands.

## Capabilities

### New Capabilities

- `personal-roster-snapshots`: personal named snapshots: capture, list, read, rename, delete, limit and names.

### Modified Capabilities

- `roster-snapshots`: the store gets a second owner (personal snapshots), and the whole-roster shape is shared by both owners.
- `upstream-call-rate-limiting`: `POST me/roster-snapshots` joins `upstream-slow` (only when it syncs).

## Impact

- `src/TacticusPlanner.Domain/RosterSnapshots`: new `PersonalRosterSnapshot` aggregate, which reuses `RosterSnapshot`.
- `src/TacticusPlanner.Persistence`: configuration, `DbSet` and migration `AddPersonalRosterSnapshots`.
- `src/TacticusPlanner.Api/Features/RosterSnapshots` (new): endpoints, validators, and a capture service that reuses the run-record clone helpers. These are extracted so both owners share one copy routine.
- `Features/PlayerData/PlayerDataSyncService`: invoked by the create endpoint, with no behavior change.
- `Features/StagingTransfer` and account purge carry the new rows, as does `Features/DebugInfo` once it exists.
- `artifacts/openapi`: new operations and schemas.
- Tests: integration tests for every endpoint, the limit, sync failure, the fallback, ownership (404 for another profile's id), purge and staging transfer.
