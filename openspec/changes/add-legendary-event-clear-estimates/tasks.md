# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-clear-estimates`. Apply this API change first; the apps change reads the dataset from the Aspire stack. Independent of `add-legendary-event-run-inputs`.

## 1. Dataset

- [ ] 1.1 Add `src/TacticusPlanner.GameCatalog/Data/lre-clear-estimate.json` with `powerRatio` 1.0, `calibration` `{ sampleCount: 0, calibratedOn: null }` and `unitCoefficients: []` (design D2), embedded like the other raw files.
- [ ] 1.2 Add `GameCatalogLreClearEstimate(Id, PowerRatio, Calibration, UnitCoefficients)` with `GameCatalogLreClearEstimateCalibration` and `GameCatalogLreUnitCoefficient` records; load it in `GameCatalogLoader`, sort coefficients by `unitId` in denormalization, add the `lre-clear-estimate` key to `GameCatalogDatasets` and the manifest, and serve it from `GetGameCatalogDatasetEndpoints.cs` (design D1, D5).
- [ ] 1.3 Add `LreClearEstimateValidation` (single record, ratio bounds, calibration pairing, character ids, coefficient bounds, duplicates) reporting `InvalidLreClearEstimate` like `GuildRaidMetaValidation` (design D3).

## 2. Tests

- [ ] 2.1 `tests/TacticusPlanner.GameCatalog.Tests/LreClearEstimateTests.cs`: initial file loads; sorting is stable across authoring orders (same hash); each validation failure in `specs/legendary-event-clear-estimate-dataset/spec.md` (unknown unit, MoW id, coefficient 0 and 5.1, ratio 0, calibration date without samples, duplicate unit) fails with the dataset and value named.
- [ ] 2.2 Update the manifest snapshot in `tests/TacticusPlanner.Api.Tests` (`GameCatalogSnapshotTests`) for the new entry.

## 3. Docs and verification

- [ ] 3.1 Add a "Legendary Event clear estimate" section to `.claude/skills/game-catalog-data` (meaning of `powerRatio` and coefficients, 1.0 baseline, bounds, change ratio and coefficients together with evidence in the PR) (design D4).
- [ ] 3.2 Verify the served dataset via `aspire run` (`GET /api/v1/game-catalog/lre-clear-estimate`) and its manifest entry.
- [ ] 3.3 Gates: `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, `git diff --check`.

## Deferred / out-of-session

- Replace the placeholder `powerRatio` and `calibration` with the values from the apps change's calibration task (a raw-file edit), once samples exist.
