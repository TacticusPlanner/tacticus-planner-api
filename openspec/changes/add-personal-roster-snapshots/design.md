## Context

`RosterSnapshot` (`TacticusPlanner.Domain/RosterSnapshots`) is today an inline jsonb value owned by `LegendaryEventRunRecord`. It holds `TakenAt`, `CatalogVersion`, `Characters` and `Mows`, copied from `PlayerDataSnapshot` by `LegendaryEventRunRecordRecorder.NewRoster`. V1 stores snapshots client-side as a base plus a chain of diffs inside the user-data blob, limited to 20 including trashed ones.

Companion: `add-personal-roster-snapshots` in `tacticus-planner-apps` consumes every endpoint below. Shared contract: the `me/roster-snapshots` resource and the existing `RosterSnapshotResponse`.

## Goals / Non-Goals

**Goals:** personal named snapshots captured from fresh synced data on the server; cheap list; immutable roster bodies; a hard per-profile limit.

**Non-Goals:** automatic snapshots (`add-automatic-roster-snapshots`); V1 import (`import-v1-roster-snapshots`); diffs computed on the server (the client owns the diff model); trash/restore; guest support (snapshots need a profile).

## Decisions

1. **One row per snapshot with the roster as jsonb**, owned by a new `PersonalRosterSnapshot` aggregate (`OwnsOne(Roster).ToJson()`, as ADR 0011 does for run records). The relational unit and equipment tables in `architecture/data/player-and-planning.md` are superseded; the docs ERD is updated in a separate docs PR. Alternative rejected: one shared `roster_snapshots` table referenced by run records. ADR 0011 removed exactly that indirection.
2. **Full copies, no diff chain.** A roster is tens of KB, so 20 per profile is cheap. Every body is independently readable and immutable after creation; only `name` changes.
3. **Sync and capture in one request, captured inside the sync's transaction.** For an existing row, `PlayerDataSyncService.WriteAsync` updates only the changed chunks and returns a metadata-only `PlayerDataSnapshot`, so the capture cannot copy that result. Instead, the endpoint runs the same sync `POST player-data/sync` runs, with a post-write step in the same `WriteAsync` transaction that builds the snapshot from the transformed `Characters`/`Mows`. This is the same input and place where `RecordRunsAsync` already opens and refreshes run records. Because the copy comes from the transform that is being written, and is committed in one transaction, no concurrent sync can land between the write and the copy. The sync path does not take the profile lock that goal writes use, and it doesn't need to for the copy. On upstream failure (503, timeout, invalid key) it returns that error and creates nothing. Alternative rejected: client syncs then calls create. That costs two round trips and opens a window where the server snapshot differs from what the client just saw.
4. **Explicit stale fallback.** `fromLastSync=true` skips upstream and loads the stored `characters` and `mows` chunks of `PlayerDataSnapshot` in one read. It needs an existing snapshot (`409 playerDataNotSynced` otherwise). It is not rate limited, because it makes no upstream call.
5. **Limit counts `Manual` (and later `V1Import`) rows only.** It is checked before any upstream call, so a request at the limit never syncs, and checked again in the capture step inside the transaction under the profile lock (the Profile row lock goal writes use), so two concurrent creates cannot exceed it. If the second check fails, the sync still commits, no snapshot is created, and the request returns the limit conflict. Error `rosterSnapshotLimitReached` (409), whose body carries `limit` and `count`. The limit (20) is configuration with a default.
6. **Names.** Optional. Blank becomes the ISO capture date (`2026-11-03`), which the client formats. Trimmed, 1–60 characters, duplicates allowed, because the id is the identity.
7. **Shared copy routine and post-write hook.** Extract `CloneCharacter`/`CloneMow`/`NewRoster` from the run-record recorder into `RosterSnapshotFactory`, so run records and personal snapshots stay byte-identical in shape. Add an optional post-write callback to `PlayerDataSyncService.SyncAsync` that receives the transformed roster inside `WriteAsync`'s transaction. `add-automatic-roster-snapshots` uses the same hook.
8. **List shape.** `{ limit, count, items: [{ id, name, source, takenAt, characterCount, mowCount }] }`, newest first. LE run-record rosters are not listed here. The client reads them through the existing run-record endpoints and shows them read-only.

## Risks / Trade-offs

- A create that syncs is limited to 6 per hour in its own bucket (buckets are per endpoint, so it does not consume `player-data/sync` quota). This is acceptable because V1 users take a few snapshots per month, and the fallback covers a rate-limited moment.
- jsonb bodies are not queryable per unit. Nothing needs that; a later projection can be added if it does.

## Migration Plan

`dotnet ef migrations add AddPersonalRosterSnapshots`: a new table with an index on `(profile_id, created_at desc)` and an FK to profiles with cascade delete. No backfill. The migration applies automatically on startup.
