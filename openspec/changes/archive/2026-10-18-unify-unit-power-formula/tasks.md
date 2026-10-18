# Tasks

## 1. Raw data and model

- [x] 1.1 Add `Data/unit-power.json`, trimmed from V1's `character-power-units.json` (global tables plus `lineup.{id}.{powerMultiplier, upgradesStatIncrease}` for catalog characters and MoWs), embed it, and verify the embedded resource loads in a test.
- [x] 1.2 Add `Models/UnitPower.cs` (served record per `specs/unit-power-dataset/spec.md`) and `GameCatalogDatasets.UnitPower = "unit-power"`, and verify the build.

## 2. Denormalization and validation

- [x] 2.1 Implement `UnitPowerDenormalizer` (sorting, `powerMultiplier` default 100, truncation to the catalog row count, `[]` for MoWs, skipping non-catalog units), and verify with denormalization tests for the Tigurius, Biovore and missing-multiplier scenarios.
- [x] 2.2 Implement `UnitPowerValidation` for every rejection listed in the spec, and verify with one validation test per rule (new character, misaligned row, unknown damage type, short curve, relic levels, non-positive multiplier).
- [x] 2.3 Register the dataset in the manifest, bump `GameCatalogRelease.SchemaVersion` to 7 and `Version`, add `GetGameCatalogUnitPowerEndpoint` (`/api/v1/game-catalog/unit-power`) and regenerate `artifacts/openapi`, update the manifest snapshot test, and verify `unit-power` appears with a hash.

## 3. Documentation

- [x] 3.1 Update `.claude/skills/game-catalog-data/SKILL.md` (and its `.agents` copy if present) with the `unit-power` mapping and refresh step, and say that a new character or MoW needs a `unit-power` entry. Verify the skill text names the V1 source files.

## 4. Gates

- [x] 4.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`, all green.

## Workflow follow-up

- Apply before the `tacticus-planner-apps` companion; review in its own thread; archive after review.
