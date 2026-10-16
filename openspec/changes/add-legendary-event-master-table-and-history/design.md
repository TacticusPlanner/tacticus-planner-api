# Design

## Context

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-master-table-and-history` (master table page, Last run card, run history on archived events). It reads the two new endpoints and the three new `lres` entries; it computes every figure (points, depths, deltas) on the client (ADR 0009).
- `player_data_snapshots` is one row per profile; `PlayerDataSyncService.SyncAsync` loads it, `ApplyTransform` overwrites the changed chunks, and `SaveChangesAsync` saves. Between the load and the transform the previous `LreProgress` (including `CurrentEventRun`) is still in memory. A unique-key retry path re-applies the transform against the winning row. `PlayerSyncEndpoint` and `ImportV1ProfileEndpoint` are the only callers.
- `lre-progress` records carry `Id` (unit snowprint id), `Alpha`/`Beta`/`Gamma` (`Encounters[] { ObjectivesCleared, HighScore, EncounterPoints }` or null), `CurrentPoints`, `CurrentEventRun` (int?) and tokens. Every LRE the Tacticus API returns is mapped, catalog or not.
- Plan routes resolve the event through `LegendaryEventPlanEndpointBase.ResolveAsync` → `LegendaryEventCatalogValidator.FindEvent`, which only searches `catalog.Current.LreViews`, so reads 404 for an id outside the current catalog.
- The catalog serves `lres` from four raw files (`GameCatalogDatasets.LreEvents`), all `finished: false`. V1 has complete generated data for ids 10–16 (`lre/data/{n}-{unit}.json` plus `new-le-battle-data.json`, adapted by `new-format-adapter.ts`) and hand-kept dates and `finished` flags in `lre-event-dates.ts` (Dante, Trajann and Lucius finished). Ids 1–9 have no battle data in V1. `GameCatalogLreLookups.ServedLreIdForV1Id` maps V1 numeric ids to served ids.

## Goals / Non-Goals

**Goals:**

- One read for every plan; plans never become unreadable because the catalog moved on.
- Keep, per run occurrence, what the player cleared, with which teams, and their roster as it was, without any manual step.
- Serve the finished events V1 has full data for.

**Non-Goals:**

- Capturing a run that starts and ends between two syncs, or backfilling runs before deploy.
- Editing or deleting records, or aggregating them across profiles.
- Any calculation over records (cleared depth, deltas): that is the client's.

## Decisions

**D1. One table, snapshots as jsonb.** `legendary_event_run_records`:

| column | type | notes |
| --- | --- | --- |
| `id` | uuid PK | |
| `profile_id` | uuid FK → profiles, cascade | query filter on the current profile |
| `event_id` | text | catalog `lres` id at capture time; no FK, so the row outlives the catalog entry |
| `run` | smallint | CHECK 1..3 |
| `started_at` | timestamptz | first sync that saw this occurrence |
| `progress_at` | timestamptz | last sync that changed its lanes |
| `catalog_version` | text | catalog version at `progress_at` |
| `points` | integer | synced `CurrentPoints` at `progress_at` |
| `lanes` | jsonb | `{ alpha, beta, gamma }`, each null or `[{ objectivesCleared: int[], highScore: int }]` in encounter order |
| `teams` | jsonb | `[{ teamId, laneId, name, sortOrder, memberUnitIds, reserveUnitId, objectiveIndexes, expectedBattleClears, expectedBattleClearsSource }]`; depth is the team's depth for this `run` or null |
| `roster` | jsonb | `[{ unitId, progressionIndex, rank, xpLevel, appliedUpgradeSlots, abilities: [{ abilityId, level }] }]` |

Index `(profile_id, event_id, started_at desc)`. Jsonb because a record is read and written whole and never queried inside; the shapes are the API's own records (`LegendaryEventRunRecordLanes`, `…TeamSnapshot`, `…RosterUnit`) mapped with `OwnsOne/OwnsMany … ToJson()` like the snapshot chunks. `encounterPoints` is not kept, because the client never uses it (synced-progress spec). `rank` is the snapshot's rank name; a MoW row has rank null.

**D2. When a record opens or refreshes.** `LegendaryEventRunRecordRecorder.Apply(db, profileId, previousLre, incomingLre, catalog, now)` runs inside `SyncAsync` after the transform and before `SaveChangesAsync`, and again on the retry path against the winner's row (it is idempotent for the same input). For each incoming record whose `Id` is a current catalog `lres` id, whose `CurrentEventRun` is 1..3, and which has at least one encounter on some lane:

1. Load the profile's latest record for that event (`started_at` desc).
2. **Open** a new record when there is none, when its `run` differs from the incoming run, or when the incoming lanes are not a superset of the stored lanes (some stored cleared id is missing for the same lane and battle, or a stored battle is gone). The last case is a reset: the same run number in a later re-run of the event.
3. Otherwise **refresh** when the incoming compact lanes differ from the stored ones.
4. Otherwise do nothing.

Opening or refreshing writes `lanes`, `points`, `progress_at = now`, `catalog_version`, the plan's teams (D3) and the roster (D4). A record therefore holds the state as of the last sync that changed that run's progress, so syncs after the run ends (progress frozen) never move its roster forward. Events outside the catalog are ignored, because the roster scope needs the event's allowed units. A record never closes explicitly; the next open supersedes it.

**D3. Teams snapshot.** The profile's plan for the event, if any, with its teams in lane then `sortOrder` order; `expectedBattleClears`/`Source` are the team's `runDepths` entry for this record's `run`, else null. No plan or no teams → `[]`.

**D4. Roster scope.** Every owned character or MoW in the incoming snapshot whose id is in the `availableUnitIds` of at least one lane of the event, ordered by `unitId`. Abilities and applied upgrade slots as synced. Locked units are absent; the client reads "absent then, owned now" as unlocked since.

**D5. Plan list and reads outside the catalog.** `GET me/legendary-event-plans` projects every stored plan of the profile (rows exist only once a write has happened, so revision ≥ 1) through the existing `LegendaryEventPlanProjection`, ordered by `event_id` ordinal. `ResolveAsync` gains a read mode: a GET for an id outside the catalog returns the stored plan when one exists, else 404. Writes keep the catalog lookup and 404. The list never adds empty plans for catalog events without a row.

**D6. Run-records read.** `GET me/legendary-event-plans/{eventId}/run-records` returns `LegendaryEventRunRecordsResponse { eventId, records: LegendaryEventRunRecordResponse[] }`, newest `started_at` first, each `{ id, run, startedAt, progressAt, catalogVersion, points, lanes, teams, roster }` with the D1 shapes in camelCase. 404 when the id is neither a catalog id nor has any record; a catalog id with no records is 200 with `records: []`. Read-only, no revision.

**D7. Finished events.** Three raw files, `lres-blooddante.json`, `lres-custotrajann.json`, `lres-emperlucius.json`, in the existing raw shape, derived from V1 `10-dante.json`, `11-trajann.json`, `12-lucius.json` and the matching `new-le-battle-data.json` entries the way `new-format-adapter.ts` builds them (track names, `killPoints`, `battlesPoints`, `defeatAll`, `allowedUnitsFilter`, `unitsRestrictions` with `index` 0..4 in V1 bonus-objective order, battles with waves), plus `finished` and `nextEventDate(Utc)` from `lre-event-dates.ts` (Dante none, Trajann 17 May 2026, Lucius 26 July 2026), `regularMissions`, `premiumMissions`, milestones, chests, `shardsPerChest` and `progression` from the per-character file. They are appended to `GameCatalogDatasets.LreEvents`. Every wave enemy id must resolve to an npc; any missing npc is added to its faction's `npcs-*.json` from the V1 datamine in the same change. `LreDenormalizer` is unchanged: the served `GameCatalogLreView` for each is built like the others (`finished` true, `eventStageStartDatesUtc` from the single date, `rewards` per event), and its battles join `lre-battles`. The `lres` and `lre-battles` hashes change; the manifest snapshot test is updated.

**D8. V1 import.** No code change. `ServedLreIdForV1Id` now finds ids 10–12, so their teams, notes, run inputs and annotations import like any served event. The same per-event outcome rules apply.

## Risks / Trade-offs

- [A run played entirely between two syncs is never recorded, and a sync gap at a run boundary records only the state last synced] → the client says history starts with synced runs; players who sync during events get full records. Detecting run ends without a sync would need a scheduled job, out of scope.
- [Reset detection by superset could open a spurious record if the game ever un-clears an objective inside a run] → not observed; the old record is kept, so nothing is lost, and the client shows the newest.
- [Roster jsonb grows with allowed units (about 100 per event)] → a few kB per record, three records per event per cycle.
- [Finished-event data comes from V1's generated files, which may drift from the game] → the same source as the active events; events re-run with fresh data replace the file.

## Migration Plan

One migration `AddLegendaryEventRunRecords` (table, CHECK on `run`, index, FK cascade). No backfill. Rollback drops the table; no other table changes.

## Open Questions

- None blocking. Whether to keep records for events removed from the catalog forever, or prune after a year, can wait for data.
