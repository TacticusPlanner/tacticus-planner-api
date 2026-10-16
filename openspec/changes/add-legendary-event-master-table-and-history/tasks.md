# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-master-table-and-history`. Apply this API change first; the apps change needs the plan list and the run-records endpoint in the Aspire stack.

## 1. Domain and persistence

- [ ] 1.1 Add `LegendaryEventRunRecord` (`Id`, `ProfileId`, `EventId`, `Run`, `StartedAt`, `ProgressAt`, `CatalogVersion`, `Points`, `Lanes`, `Teams`, `Roster`) and the owned records `LegendaryEventRunRecordLanes`, `…Encounter`, `…TeamSnapshot`, `…RosterUnit`, `…Ability` under `src/TacticusPlanner.Domain/LegendaryEvents/` (design D1).
- [ ] 1.2 EF configuration: table `legendary_event_run_records`, jsonb `lanes`/`teams`/`roster` via `ToJson()`, CHECK `run` 1–3, index `(profile_id, event_id, started_at desc)`, FK cascade from profiles, query filter on the current profile in `PlannerDbContext`.
- [ ] 1.3 Migration `dotnet ef migrations add AddLegendaryEventRunRecords`; add a Postgres integration test asserting the CHECK rejects run 4 and account purge removes records.

## 2. Capture at sync

- [ ] 2.1 Add `Features/LegendaryEventPlans/LegendaryEventRunRecordRecorder.cs` with the open / refresh / no-op rule, the superset reset check, the teams snapshot with the run's depth and the roster scoped to the event's `availableUnitIds` (design D2–D4).
- [ ] 2.2 Call it from `PlayerDataSyncService.SyncAsync` after `ApplyTransform` and before `SaveChangesAsync`, and on the unique-key retry path against the winner's row; verify a retried sync leaves one record.
- [ ] 2.3 Tests (`LegendaryEventRunRecordRecorderTests.cs` unit, plus a sync endpoint test) for every scenario of "A run record is opened or refreshed on every sync" and "A run record stores lanes, teams and roster as of its last progress".

## 3. Endpoints

- [ ] 3.1 Add read mode to `LegendaryEventPlanEndpointBase.ResolveAsync`: a GET for an id outside the catalog returns the stored plan when one exists, else 404; writes unchanged (design D5).
- [ ] 3.2 Add `GET me/legendary-event-plans` (`LegendaryEventPlanListResponse`) through `LegendaryEventPlanProjection`, ordered by `eventId` ordinal, with `Summary(...)`.
- [ ] 3.3 Add `GET me/legendary-event-plans/{eventId}/run-records` (`LegendaryEventRunRecordsResponse`, newest first; 200 empty for a catalog id; 404 when neither catalog nor records) (design D6).
- [ ] 3.4 Endpoint tests in `LegendaryEventPlanEndpointTests.cs` (or a sibling) for every scenario of the `legendary-event-plans` delta (stored plan outlives catalog, writes 404, list ordering, empty list, profile isolation) and of "Run records are readable per event".
- [ ] 3.5 Verify the regenerated `artifacts/openapi/TacticusPlanner.Api.json` carries both endpoints and their response schemas, and that the apps change's hand-written types match it.

## 4. Docs

- [ ] 4.1 In `tacticus-planner-docs`: add `legendary_event_run_records` to `architecture/data/events.md`; mark the feature spec's Stage 7 API bullets done and drop its finished-events rule; add the between-syncs capture risk to plan §8.

## 5. Gates

- [ ] 5.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`.

## Deferred / out-of-session

- [ ] Live verification of record capture across a real run boundary needs a Legendary Event run to change while a synced account is available; run it on the next run start and note the result on the PR.
