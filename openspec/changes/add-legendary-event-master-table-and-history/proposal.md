# Proposal

## Why

Stage 7 of the V2 Legendary Event plan ("Cross-event: master table and history", docs plan §5) covers two survey rows the earlier stages leave open: "cross-event prioritisation" (which units are worth investing in across every event I care about) and "team reuse" (pain #6: on an event's next run or re-run, what did I clear last time, with which teams, and what has changed on my roster since). The apps half (master table, Last run card) needs two things the API does not provide today.

1. **Every plan in one read.** The master table shows team membership for units across several events; today plans are only readable one event at a time (`GET /me/legendary-event-plans/{eventId}`), and reads 404 for any id outside the current catalog.
2. **A record of each run.** The player snapshot (`player_data_snapshots`) is one row per profile, overwritten on every sync, so a run's cleared battles are lost the moment the next run resets them, and nothing records the roster or the teams as they were. V1 never had history either (one progress entry per event, teams once per event), so there is nothing to import.

## What Changes

- **Plan list.** New `GET /me/legendary-event-plans` returns every stored plan of the caller in the existing `LegendaryEventPlanResponse` shape, ordered by `eventId`. A stored plan stays readable (list and single read) after its event leaves the catalog; writes to such a plan still 404.
- **Run records captured at sync.** New table `legendary_event_run_records` (profile × event × run occurrence). On every successful player sync, for each catalog event in the incoming `lre-progress` chunk with a `currentEventRun`, the server opens a record when the run number changes or progress resets, and refreshes it whenever that event's lane progress changes. A record keeps the run number, when it was first seen and last changed, the event points, the compact per-lane encounters (`objectivesCleared`, `highScore`), the plan's teams with their depth for that run, and the progression of every owned unit the event allows, all as of the last sync that changed that run's progress. Written in the same transaction as the snapshot. Migration included; no backfill (history starts with the first sync after deploy).
- **Run records read.** New `GET /me/legendary-event-plans/{eventId}/run-records` returns the event's records, newest first.
- **OpenAPI artifact** regenerated.

Out of scope: the master table, tier list and history UI (client side, ADR 0009), adding finished events to the catalog (Severyn, 2026-10-16: not needed), any community aggregate of run records, editing or deleting run records, and capturing runs that start and end between two syncs (see design risks).

## Capabilities

### New Capabilities

- `legendary-event-run-records`: when a run record is opened and refreshed during sync, what it stores, its read endpoint and served shape.

### Modified Capabilities

- `legendary-event-plans`: plan list endpoint; stored plans stay readable when their event leaves the catalog.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-master-table-and-history`; apply API first. Shared contracts: `GET /api/v1/me/legendary-event-plans` (`LegendaryEventPlanListResponse`), `GET /api/v1/me/legendary-event-plans/{eventId}/run-records` (`LegendaryEventRunRecordsResponse`).
- `src/TacticusPlanner.Domain/LegendaryEvents/` (new `LegendaryEventRunRecord`), `src/TacticusPlanner.Persistence` (configuration, query filter, one migration), `src/TacticusPlanner.Api/Features/PlayerData/PlayerDataSyncService.cs` (record capture), new `Features/LegendaryEventPlans/LegendaryEventRunRecordRecorder.cs`, `Features/LegendaryEventPlans/*` (list and run-records endpoints, read resolution), `artifacts/openapi/TacticusPlanner.Api.json`.
- Tests: `tests/TacticusPlanner.Api.Tests` (list, read after catalog drop, run-records endpoint, sync capture), `tests/TacticusPlanner.Persistence.IntegrationTests` (migration, constraints, cascade).
- No infra change. The apps change also adds a Tier List page, which reads the already-served `horde-mode-tiers` dataset and needs nothing here.
- Docs: `architecture/data/events.md` gains `legendary_event_run_records`; the feature spec's Stage 7 bullets are marked done and its "finished events enter the catalog" rule is dropped; plan §8 gains the between-syncs capture risk.
