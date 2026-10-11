# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-run-inputs`. Apply this API change first; the apps change needs the run endpoint and `lres[].rewards` in the Aspire stack, and the removal of `lre-common` breaks the shipped run status card until the apps change lands.

## 1. Catalog: per-event reward ladder

- [ ] 1.1 Add `GameCatalogLreRewards(PointsMilestones, ChestsMilestones, Progression, ShardsPerChest)` and a `Rewards` member on `GameCatalogLreView` in `src/TacticusPlanner.GameCatalog/Models/Lres.cs`; populate it in `BuildLres` from each event's raw record (design D5).
- [ ] 1.2 Remove `GameCatalogLreCommon`, `BuildLreCommon`, the `LreCommon` dataset key, its snapshot/manifest entry and its route in `GetGameCatalogDatasetEndpoints.cs`; verify no remaining reference with `rg -n "LreCommon|lre-common" src tests`.
- [ ] 1.3 Add ladder validation to the LRE load path (milestones 1..n ascending by `cumulativePoints`, chest levels 1..n, positive payouts/costs/progression/`shardsPerChest`) reporting the event file and dataset like the existing validators.
- [ ] 1.4 Tests in `tests/TacticusPlanner.GameCatalog.Tests` (new `LreDenormalizerTests.cs`): `votanUthar.rewards` equals its raw values; `astarLysander` has 60 chest rungs summing to 44,450 and `votanUthar` 54 summing to 35,450; a malformed fixture ladder fails validation. Update `tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests` (manifest snapshot) and `GameCatalogLoaderTests` for the removed dataset and the new `lres` hash.

## 2. Domain and persistence

- [ ] 2.1 Add `LegendaryEventRunProgress` (`PlanId`, `Run`, `RegularMissions`, `PremiumMissions`, `BundlePurchased`, `CloseShards`, `UpdatedAt`) and `LegendaryEventObjectiveAnnotation` (`PlanId`, `LaneId`, `BattleIndex`, `ObjectiveId`, `Status`, `UpdatedAt`) under `src/TacticusPlanner.Domain/LegendaryEvents/`, with `Runs` and `Annotations` collections on `LegendaryEventPlan` (design D1, D6).
- [ ] 2.2 EF configuration: table `legendary_event_run_progress`, PK `(plan_id, run)`, CHECKs (run 1–3, non-negative counts); table `legendary_event_objective_annotations`, PK `(plan_id, lane_id, battle_index, objective_id)`, CHECKs (lane, `battle_index >= 0`, objective 0–5, status); FK cascade from the plan for both; query filter `e.Plan!.ProfileId == CurrentProfileId` in `PlannerDbContext`.
- [ ] 2.3 Migration `dotnet ef migrations add AddLegendaryEventRunInputsAndAnnotations`; extend `LegendaryEventPlansMigrationPostgresTests` (or a sibling) to assert the CHECKs reject run 4, a negative count, objective 6 and an unknown status, and that account purge removes run and annotation rows.

## 3. Run endpoint and served shape

- [ ] 3.1 Add `LegendaryEventRunProgressResponse` and `runs` to `LegendaryEventPlanResponse`; extend `LegendaryEventPlanProjection` to load and order run rows (design D4).
- [ ] 3.2 Extend `LegendaryEventCatalogValidator` with run-input checks (run 1–3; missions against the event's `RegularMissions.Count` / `PremiumMissions.Count`; close shards 0–75) and unit-test them in `LegendaryEventCatalogValidatorTests.cs` (design D3).
- [ ] 3.3 Add `PUT me/legendary-event-plans/{eventId}/runs/{run}` through `LegendaryEventPlanWriter`: upsert the run row, leave other runs, keep the revision when values are unchanged (or all zero for an absent row), lazy-create at revision 0, 409 on stale revision; FastEndpoints validator and `Summary(...)` (design D2).
- [ ] 3.4 Add `PUT me/legendary-event-plans/{eventId}/annotations` through `LegendaryEventPlanWriter` with validation against the lane's battle and objective counts: set or clear the listed cells of one battle, keep the revision when no cell changes, lazy-create, 409 on stale revision; add `annotations` to the response and projection ordered lane, battle, objective (design D4, D6).
- [ ] 3.5 Endpoint tests in `LegendaryEventPlanEndpointTests.cs` for every scenario in `specs/legendary-event-plans/spec.md`: new-plan write, run isolation, each 400, unchanged values keep the revision, stale 409 on a run write, `runs` ordering and empty `runs`, every annotation scenario (mark one cell, whole battle then clear one, each 400, unchanged keeps the revision, ordering).

## 4. V1 import

- [ ] 4.1 Extend `TacticusV1Client`: `V1LreProgress.Overview` (`V1LreOccurrence { RegularMissions, PremiumMissions, Bundle, OhSoCloseShards }`, loosely typed), `CompactProgress` (`States`, `Statuses` only) and legacy `BattlesProgress` (`TrackId`, `BattleIndex`, `Requirements[{ Id, State, Status }]`), `V1UserData.LeSettings { ShowP2POptions }`, and the read of both into `V1LegendaryEventImportData` (design D7).
- [ ] 4.2 In `V1LegendaryEventImportService`: iterate the union of teams and progress event keys in numeric order; import run inputs with clamping and the `run_input_clamped`, `unknown_run`, `existing_run_inputs_kept` issues; write run inputs and notes even when the plan already has teams; set `showPaidOptions` only on plans the import creates; import annotations (statuses 2/3 and legacy blocked state; `_defeatAll`/`_killPoints`/`_highScore` → 0, names through `V1LegendaryEventObjectiveNames.Matches`; `stopHere` wins on one cell; V2 cells kept) with the `unknown_lane`, `unknown_battle`, `unknown_objective`, `existing_annotations_kept` issues; add `RunInputsImported`, `AnnotationsImported` and the `inputs_imported` code; keep the part summary rules (design D7).
- [ ] 4.3 Tests in `V1LegendaryEventImportEndpointTests.cs` for every scenario in `specs/v1-legendary-event-import/spec.md` (overview with clamp, inputs on a plan with teams, existing inputs kept, paid options new vs existing plan, progress-only event, re-import skip, notes to a plan with teams, missing keys, compact statuses, legacy blocked state, unknown objective name, existing annotation kept); verify the Stage 2 import tests still pass.

## 5. Contract and verification

- [ ] 5.1 Build and verify `artifacts/openapi/TacticusPlanner.Api.json` carries the run and annotation routes, `runs` and `annotations` on the plan response and the new outcome fields; diff it against design D4 and note naming differences for the apps change.
- [ ] 5.2 Live check via `aspire run`: write run 2 inputs for `votanUthar` on a provisioned profile, confirm the plan echoes them, mark and clear an annotation, a stale revision returns 409, and the manifest lists no `lre-common`.
- [ ] 5.3 Gates: `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, `git diff --check`.

## Deferred / out-of-session

- Amend `tacticus-planner-docs` when this change is archived: `architecture/data/events.md` (run progress and objective annotations implemented, replacing the proposed battle/objective progress tables; `lre-common` removed), `product/features/lre-planning.md` open question on `MaybeClear` / `StopHere` (kept, uncleared cells only), plan §8 ladder risk resolved.
