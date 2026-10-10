# Proposal

## Why

Stage 5 of the V2 Legendary Event plan ("Clear-depth estimation", docs plan §5; survey pain #2, "users must guess how far each team gets"). The apps change estimates how many battles a team, or a single unit, clears on a lane from combat power, a calibrated power ratio and a per-unit efficiency coefficient. The Stage 1 follow-up (2026-10-07) decided the coefficients are **curated catalog data**, served like `guild-raid-meta`'s `efficiency`, so tuning never needs a client release; that dataset was deferred from Stage 1 and never opened, so it is part of this change. The calibrated power ratio is the other tuning parameter and belongs in the same place (ADR 0009 allows catalog-served parameters for client calculations).

## What Changes

- **New served dataset `lre-clear-estimate`**: one record with the power ratio, the calibration metadata (sample count, date) and per-unit efficiency coefficients for Legendary Event battles. A unit absent from the list has coefficient 1.0, so an empty list is valid and is the starting state.
- **Raw file** `Data/lre-clear-estimate.json`, validated on load (unit ids exist as characters, coefficients and ratio positive and bounded, no duplicates), listed in the public manifest with its hash.
- No calculation endpoint, table or migration; the dataset is served by the existing catalog route as `GET /api/v1/game-catalog/lre-clear-estimate`. No server-side calculation (ADR 0009). The estimate is not persisted (apps design D2).

Out of scope: the estimate calculation and UI (companion apps change), the anonymised community aggregate ("players with team power P cleared battle X"; plan §5 "later, separate change", needs a privacy review), and who curates coefficients long-term (open question).

## Capabilities

### New Capabilities

- `legendary-event-clear-estimate-dataset`: the served `lre-clear-estimate` record, its fields, defaults and validation.

### Modified Capabilities

- None.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-clear-estimates`; apply API first. Shared contract: the `lre-clear-estimate` dataset shape in the spec.
- `src/TacticusPlanner.GameCatalog` (`Data/lre-clear-estimate.json`, model record, loader, a validator, `GameCatalogDatasets` key, manifest), `src/TacticusPlanner.Api/Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` (route), the `game-catalog-data` skill (curation notes).
- Tests: `tests/TacticusPlanner.GameCatalog.Tests` (load, validation), `tests/TacticusPlanner.Api.Tests` manifest snapshot.
- No infra change. No migration.
- Independent of `add-legendary-event-run-inputs` (Stage 3); either can apply first.
