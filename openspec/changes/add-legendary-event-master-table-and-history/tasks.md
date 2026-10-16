# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-master-table-and-history`. Apply this API change first; the apps change needs the plan list, the run-records endpoint and the three finished events in the Aspire stack.

## 1. Catalog: finished events

- [ ] 1.1 Derive `lres-blooddante.json`, `lres-custotrajann.json` and `lres-emperlucius.json` in `src/TacticusPlanner.GameCatalog/Data/lres/` from V1 `src/fsd/4-entities/lre/data/{10-dante,11-trajann,12-lucius}.json`, their `new-le-battle-data.json` entries and `lre-event-dates.ts`, following `new-format-adapter.ts` and the `game-catalog-data` skill; append them to `GameCatalogDatasets.LreEvents` (design D7).
- [ ] 1.2 Resolve every wave enemy id against `Data/npcs/*`; add any missing npc from the V1 datamine to its faction file; verify the catalog loads with `ReferenceValidation` clean.
- [ ] 1.3 Tests in `tests/TacticusPlanner.GameCatalog.Tests`: `bloodDante` served with `finished` true, empty `eventStageStartDatesUtc`, 3 × 18 battles and objective indexes 0–4; `custoTrajann` date `2026-05-17T00:00:00Z`; `ServedLreIdForV1Id(10|11|12)`; no V1 id 1–9 served. Update the manifest snapshot test (`lres`, `lre-battles`, and `npcs` hashes if npcs were added).
- [ ] 1.4 Fix the `game-catalog-data` skill's LRE section: rewards are per event (no `lre-common`), and finished events are added like active ones with `finished: true`.

## 2. Domain and persistence

- [ ] 2.1 Add `LegendaryEventRunRecord` (`Id`, `ProfileId`, `EventId`, `Run`, `StartedAt`, `ProgressAt`, `CatalogVersion`, `Points`, `Lanes`, `Teams`, `Roster`) and the owned records `LegendaryEventRunRecordLanes`, `…Encounter`, `…TeamSnapshot`, `…RosterUnit`, `…Ability` under `src/TacticusPlanner.Domain/LegendaryEvents/` (design D1).
- [ ] 2.2 EF configuration: table `legendary_event_run_records`, jsonb `lanes`/`teams`/`roster` via `ToJson()`, CHECK `run` 1–3, index `(profile_id, event_id, started_at desc)`, FK cascade from profiles, query filter on the current profile in `PlannerDbContext`.
- [ ] 2.3 Migration `dotnet ef migrations add AddLegendaryEventRunRecords`; add a Postgres integration test asserting the CHECK rejects run 4 and account purge removes records.

## 3. Capture at sync

- [ ] 3.1 Add `Features/LegendaryEventPlans/LegendaryEventRunRecordRecorder.cs` with the open / refresh / no-op rule, the superset reset check, the teams snapshot with the run's depth and the roster scoped to the event's `availableUnitIds` (design D2–D4).
- [ ] 3.2 Call it from `PlayerDataSyncService.SyncAsync` after `ApplyTransform` and before `SaveChangesAsync`, and on the unique-key retry path against the winner's row; verify a retried sync leaves one record.
- [ ] 3.3 Tests (`LegendaryEventRunRecordRecorderTests.cs` unit, plus a sync endpoint test) for every scenario of "A run record is opened or refreshed on every sync" and "A run record stores lanes, teams and roster as of its last progress".

## 4. Endpoints

- [ ] 4.1 Add read mode to `LegendaryEventPlanEndpointBase.ResolveAsync`: a GET for an id outside the catalog returns the stored plan when one exists, else 404; writes unchanged (design D5).
- [ ] 4.2 Add `GET me/legendary-event-plans` (`LegendaryEventPlanListResponse`) through `LegendaryEventPlanProjection`, ordered by `eventId` ordinal, with `Summary(...)`.
- [ ] 4.3 Add `GET me/legendary-event-plans/{eventId}/run-records` (`LegendaryEventRunRecordsResponse`, newest first; 200 empty for a catalog id; 404 when neither catalog nor records) (design D6).
- [ ] 4.4 Endpoint tests in `LegendaryEventPlanEndpointTests.cs` (or a sibling) for every scenario of the `legendary-event-plans` delta (stored plan outlives catalog, writes 404, list ordering, empty list, profile isolation) and of "Run records are readable per event".
- [ ] 4.5 Verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json` carries both endpoints and their response schemas, and that the apps change's hand-written types match it.

## 5. V1 import

- [ ] 5.1 Move the "finished event" test in `V1LegendaryEventImportEndpointTests.cs` to Kharn (V1 id 7) and add a Dante (V1 id 10) import test against `bloodDante`; verify the existing import tests still pass (design D8).

## 6. Docs

- [ ] 6.1 In `tacticus-planner-docs`: add `legendary_event_run_records` to `architecture/data/events.md`; mark the feature spec's finished-events rule and the Stage 7 API bullets done; add the between-syncs capture risk to plan §8.

## 7. Gates

- [ ] 7.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.

## Deferred / out-of-session

- [ ] Live verification of record capture across a real run boundary needs a Legendary Event run to change while a synced account is available; run it on the next run start and note the result on the PR.
