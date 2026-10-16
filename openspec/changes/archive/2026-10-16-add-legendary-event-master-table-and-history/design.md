# Design

## Context

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-master-table-and-history` (master table page, Last run card, run history on archived events). It reads the two new endpoints; it computes every figure (points, depths, deltas) on the client (ADR 0009).
- `player_data_snapshots` is one row per profile; `PlayerDataSyncService.SyncAsync` loads it, `ApplyTransform` overwrites the changed chunks, and `SaveChangesAsync` saves. Between the load and the transform the previous `LreProgress` (including `CurrentEventRun`) is still in memory. A unique-key retry path re-applies the transform against the winning row. `PlayerSyncEndpoint` and `ImportV1ProfileEndpoint` are the only callers.
- `lre-progress` records carry `Id` (unit snowprint id), `Alpha`/`Beta`/`Gamma` (`Encounters[] { ObjectivesCleared, HighScore, EncounterPoints }` or null), `CurrentPoints`, `CurrentEventRun` (int?) and tokens. Every LRE the Tacticus API returns is mapped, catalog or not.
- Plan routes resolve the event through `LegendaryEventPlanEndpointBase.ResolveAsync` → `LegendaryEventCatalogValidator.FindEvent`, which only searches `catalog.Current.LreViews`, so reads 404 for an id outside the current catalog.

## Goals / Non-Goals

**Goals:**

- One read for every plan; plans never become unreadable because the catalog moved on.
- Keep, per run occurrence, what the player cleared, with which teams, and their roster as it was, without any manual step.

**Non-Goals:**

- Manual or named roster snapshots, their endpoints and the V1 `rosterSnapshots` import (the later port of V1's roster snapshots feature builds on D4's table).
- Capturing a run that starts and ends between two syncs, or backfilling runs before deploy.
- Editing or deleting records, or aggregating them across profiles.
- Adding finished events to the catalog (not needed, Severyn 2026-10-16).
- Any calculation over records (cleared depth, deltas): that is the client's.

## Decisions

**D1. One table, snapshots as jsonb.** `legendary_event_run_records`:

| column               | type                                                     | notes                                                                                                                                                                                       |
| -------------------- | -------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                 | uuid PK                                                  |                                                                                                                                                                                             |
| `profile_id`         | uuid FK → profiles, cascade                              | query filter on the current profile                                                                                                                                                         |
| `event_id`           | text                                                     | catalog `lres` id at capture time; no FK, so the row outlives the catalog entry                                                                                                             |
| `run`                | integer                                                  | CHECK 1..3                                                                                                                                                                                  |
| `started_at`         | timestamptz                                              | first sync that saw this occurrence                                                                                                                                                         |
| `progress_at`        | timestamptz                                              | last sync that changed its lanes                                                                                                                                                            |
| `catalog_version`    | text                                                     | catalog version at `progress_at`                                                                                                                                                            |
| `points`             | integer                                                  | synced `CurrentPoints` at `progress_at`                                                                                                                                                     |
| `lanes`              | jsonb                                                    | `{ alpha, beta, gamma }`, each null or `[{ objectivesCleared: int[], highScore: int }]` in encounter order                                                                                  |
| `teams`              | jsonb                                                    | `[{ teamId, laneId, name, sortOrder, memberUnitIds, reserveUnitId, objectiveIndexes, expectedBattleClears, expectedBattleClearsSource }]`; depth is the team's depth for this `run` or null |
| `roster_snapshot_id` | uuid FK → `roster_snapshots`, set null on delete, unique | the run's roster, see D4                                                                                                                                                                    |

Index `(profile_id, event_id, started_at desc)`. Jsonb because a record is read and written whole and never queried inside; the shapes are the API's own records (`LegendaryEventRunRecordLanes`, `…TeamSnapshot`) mapped with `OwnsOne/OwnsMany … ToJson()` like the snapshot chunks. `encounterPoints` is not kept, because the client never uses it (synced-progress spec).

**D2. When a record opens or refreshes.** `LegendaryEventRunRecordRecorder.Apply(db, profileId, previousLre, incomingLre, catalog, now)` runs inside `SyncAsync` after the transform and before `SaveChangesAsync`, and again on the retry path against the winner's row (it is idempotent for the same input). For each incoming record whose `Id` is a current catalog `lres` id, whose `CurrentEventRun` is 1..3, and which has at least one encounter on some lane:

1. Load the profile's latest record for that event (`started_at` desc).
2. **Open** a new record when there is none, when its `run` differs from the incoming run, or when the incoming lanes are not a superset of the stored lanes (some stored cleared id is missing for the same lane and battle, or a stored battle is gone). The last case is a reset: the same run number in a later re-run of the event.
3. Otherwise **refresh** when the incoming compact lanes differ from the stored ones.
4. Otherwise do nothing.

Opening or refreshing writes `lanes`, `points`, `progress_at = now`, `catalog_version`, the plan's teams (D3) and the run's roster snapshot (D4). A record therefore holds the state as of the last sync that changed that run's progress, so syncs after the run ends (progress frozen) never move its roster forward. Events outside the catalog are ignored. A record never closes explicitly; the next open supersedes it.

**D3. Teams snapshot.** The profile's plan for the event, if any, with its teams in lane then `sortOrder` order; `expectedBattleClears`/`Source` are the team's `runDepths` entry for this record's `run`, else null. No plan or no teams → `[]`.

**D4. Roster snapshots are their own table (Severyn, 2026-10-16).** V1 has a user-facing roster snapshots feature (`/input/roster-snapshots`: named snapshots of the whole roster, compared unit by unit) that V2 will port later. So the run's roster is not a column of the run record; it is a row of a general `roster_snapshots` table that this change introduces as the foundation, with only the run-record source wired up:

| column            | type                        | notes                                                                                                                                                                                                            |
| ----------------- | --------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`              | uuid PK                     |                                                                                                                                                                                                                  |
| `profile_id`      | uuid FK → profiles, cascade | query filter on the current profile                                                                                                                                                                              |
| `source`          | text                        | CHECK in (`legendaryEventRun`); the later port adds `manual`                                                                                                                                                     |
| `name`            | text null                   | CHECK length ≤ 60; null for `legendaryEventRun`, the user's name for a later manual snapshot                                                                                                                     |
| `taken_at`        | timestamptz                 | when the units were read                                                                                                                                                                                         |
| `catalog_version` | text                        | catalog version at `taken_at`                                                                                                                                                                                    |
| `characters`      | jsonb                       | every owned character, by `unitId`: `{ unitId, progressionIndex, rank, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots, equippedItems: [{ slotId, equipmentId, level }] }` |
| `mows`            | jsonb                       | every owned MoW, by `unitId`: `{ unitId, progressionIndex, xpLevel, shards, mythicShards, abilities: [{ abilityId, level }], appliedUpgradeSlots }`                                                              |

Index `(profile_id, taken_at desc)`. The unit shapes are the synced `characters` / `mows` chunk records, so a later diff view compares like with like, and they cover every field V1's snapshot diff shows (rank, rarity and stars via progression, shards, mythic shards, XP level, abilities, equipment). Each row is a full roster, not V1's chain of diffs against a base: V1 diffed to keep its localStorage blob small, which a database row does not need, and full rows can be deleted independently. A snapshot is the whole owned roster, not only the event's units, so the same row serves the later feature as is.

A run record points at its snapshot through `roster_snapshot_id` (unique: a `legendaryEventRun` snapshot belongs to exactly one record). Opening a record inserts a snapshot; refreshing it rewrites that same snapshot row (`taken_at`, `catalog_version`, units), so a run never piles up snapshots. If a snapshot is ever removed (the later manual feature may allow it), the record keeps its lanes and teams with `roster_snapshot_id` null. No roster-snapshot endpoint ships yet; the run-records read embeds the snapshot (D6).

**D5. Plan list and reads outside the catalog.** `GET me/legendary-event-plans` projects every stored plan of the profile (rows exist only once a write has happened, so revision ≥ 1) through the existing `LegendaryEventPlanProjection`, ordered by `event_id` ordinal. `ResolveAsync` gains a read mode: a GET for an id outside the catalog returns the stored plan when one exists, else 404. Writes keep the catalog lookup and 404. The list never adds empty plans for catalog events without a row.

**D6. Run-records read.** `GET me/legendary-event-plans/{eventId}/run-records` returns `LegendaryEventRunRecordsResponse { eventId, records: LegendaryEventRunRecordResponse[] }`, newest `started_at` first, each `{ id, run, startedAt, progressAt, catalogVersion, points, lanes, teams, rosterSnapshot }` with the D1 shapes in camelCase and `rosterSnapshot` the joined D4 row as `{ id, takenAt, catalogVersion, characters, mows }`, or null. 404 when the id is neither a catalog id nor has any record; a catalog id with no records is 200 with `records: []`. Read-only, no revision.

## Risks / Trade-offs

- [A run played entirely between two syncs is never recorded, and a sync gap at a run boundary records only the state last synced] → the client says history starts with synced runs; players who sync during events get full records. Detecting run ends without a sync would need a scheduled job, out of scope.
- [Reset detection by superset could open a spurious record if the game ever un-clears an objective inside a run] → not observed; the old record is kept, so nothing is lost, and the client shows the newest.
- [A full-roster snapshot per run is larger than an event-scoped one (about 120 characters plus MoWs)] → tens of kB per row, one row per run occurrence, rewritten rather than appended; it buys a snapshot the later roster-snapshots feature can reuse unchanged.

## Migration Plan

One migration `AddLegendaryEventRunRecords` (`roster_snapshots` and `legendary_event_run_records` with their CHECKs, indexes and FKs). No backfill. Rollback drops both tables; no other table changes.

## Open Questions

- None blocking. Whether to keep records for events removed from the catalog forever, or prune after a year, can wait for data.
