# Tasks

Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-clear-estimates`. Apply this API change first; the apps change reads the dataset from the Aspire stack. Independent of `add-legendary-event-run-inputs`.

## 1. Dataset

- [x] 1.1 Add `Data/horde-mode-tiers.json` with the S, A, B, C multipliers and the seed tier list (design D2); `GameCatalogHordeModeTiers(Id, Tiers, Characters)` with `GameCatalogHordeModeTier` and `GameCatalogHordeModeCharacter(UnitId, Tier, Multiplier?)` (override omitted from JSON when null); `HordeModeTiersDenormalizer` sorting `characters` by `unitId`; the `horde-mode-tiers` key, manifest entry and route (design D1, D6).
- [x] 1.2 Add `HordeModeTiersValidation` (single record, id, tier names and order, tier multiplier bounds, character ids, duplicates, known tier, override bounds) reporting `InvalidHordeModeTiers` (design D4).
- [x] 1.3 Remove the `legendary-event-clear-estimate` dataset (raw file, model, validation, key, route, tests) per design D3.

## 2. Tests

- [x] 2.1 `tests/TacticusPlanner.GameCatalog.Tests/HordeModeTiersTests.cs`: initial file loads; sorting is stable across authoring orders (same hash); null lists serve empty; overrides deserialize and serialize only when set; each validation failure in `specs/horde-mode-tiers-dataset/spec.md` (unknown unit, MoW id, duplicate, unknown tier, tiers out of order or missing, tier multiplier and override 0 and 5.1) fails with the dataset and value named.
- [x] 2.2 `tests/TacticusPlanner.Api.Tests` (`GameCatalogSnapshotTests`): the dataset is served anonymously with the expected shape; update the manifest snapshot.

## 3. Docs and verification

- [x] 3.1 Add a "Horde-mode tiers" section to the `game-catalog-data` skill (meaning of tiers and overrides, 1.0 baseline, bounds, evidence in the PR) (design D5).
- [x] 3.2 Verify the served dataset (`GET /api/v1/game-catalog/horde-mode-tiers`) and its manifest entry. Verified in this session through `GameCatalogSnapshotTests` because the cloud session has no Docker for the Aspire PostgreSQL resource.
- [x] 3.3 Gates: `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build`, `dotnet test` (GameCatalog and Api test projects), `git diff --check`.

## Deferred / out-of-session

- Re-weight the tier multipliers and overrides from the apps change's calibration evidence, as raw-file edits.
