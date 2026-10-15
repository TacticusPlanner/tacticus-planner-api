# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-clear-estimates`. Apply this API change first; the apps change reads the datasets from the Aspire stack. Independent of `add-legendary-event-run-inputs`.

## 1. Datasets

- [x] 1.1 Add `src/TacticusPlanner.GameCatalog/Data/legendary-event-clear-estimate.json` with `powerRatio` 1.0 and `calibration` `{ sampleCount: 0, calibratedOn: null }` (design D3), embedded like the other raw files.
- [x] 1.2 Add `GameCatalogLegendaryEventClearEstimate(Id, PowerRatio, Calibration)` with `GameCatalogLegendaryEventClearEstimateCalibration`; load it in `GameCatalogLoader`, add the `legendary-event-clear-estimate` key to `GameCatalogDatasets` and the manifest, and serve it from `GetGameCatalogDatasetEndpoints.cs` (design D1, D6).
- [x] 1.3 Add `LegendaryEventClearEstimateValidation` (single record, id, ratio bounds, calibration presence and pairing) reporting `InvalidLegendaryEventClearEstimate` (design D4).
- [x] 1.4 Add `Data/horde-mode-tiers.json` with the S, A, B, C multipliers and the seed tier list (design D3); `GameCatalogHordeModeTiers(Id, Tiers, Characters)` with `GameCatalogHordeModeTier` and `GameCatalogHordeModeCharacter`; `HordeModeTiersDenormalizer` sorting `characters` by `unitId`; the `horde-mode-tiers` key, manifest entry and route (design D2, D6).
- [x] 1.5 Add `HordeModeTiersValidation` (single record, id, tier names and order, multiplier bounds, character ids, duplicates, known tier) reporting `InvalidHordeModeTiers` (design D4).

## 2. Tests

- [x] 2.1 `tests/TacticusPlanner.GameCatalog.Tests/LegendaryEventClearEstimateTests.cs`: initial file loads; each validation failure in `specs/legendary-event-clear-estimate-dataset/spec.md` fails with the dataset and value named.
- [x] 2.2 `tests/TacticusPlanner.GameCatalog.Tests/HordeModeTiersTests.cs`: initial file loads; sorting is stable across authoring orders (same hash); null lists serve empty; each validation failure in `specs/horde-mode-tiers-dataset/spec.md` (unknown unit, MoW id, duplicate, unknown tier, tiers out of order or missing, multiplier 0 and 5.1) fails with the dataset and value named.
- [x] 2.3 `tests/TacticusPlanner.Api.Tests` (`GameCatalogSnapshotTests`): both datasets are served anonymously with the expected shape; update the manifest snapshot.

## 3. Docs and verification

- [x] 3.1 Add "Legendary Event clear estimate" and "Horde-mode tiers" sections to the `game-catalog-data` skill (meaning of `powerRatio` and tiers, 1.0 baseline, bounds, change ratio and tiers together with evidence in the PR) (design D5).
- [x] 3.2 Verify the served datasets (`GET /api/v1/game-catalog/legendary-event-clear-estimate`, `GET /api/v1/game-catalog/horde-mode-tiers`) and their manifest entries. Verified in this session through `GameCatalogSnapshotTests` because the cloud session has no Docker for the Aspire PostgreSQL resource.
- [x] 3.3 Gates: `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build`, `dotnet test` (GameCatalog and Api test projects), `git diff --check`.

## Deferred / out-of-session

- Replace the placeholder `powerRatio`, `calibration` and tier multipliers with values from the apps change's calibration task (raw-file edits), once samples exist.
