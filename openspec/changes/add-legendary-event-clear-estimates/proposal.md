# Proposal

## Why

Stage 5 of the V2 Legendary Event plan ("Clear-depth estimation", docs plan §5; survey pain #2, "users must guess how far each team gets"). The apps change estimates how many battles a team, or a single unit, clears on a lane from combat power, a calibrated power ratio and a per-character horde-mode tier. The Stage 1 follow-up (2026-10-07) decided per-unit tuning is **curated catalog data**, so it never needs a client release; the calibrated power ratio is the other tuning parameter and belongs in the same place (ADR 0009 allows catalog-served parameters for client calculations).

Review (2026-10-15) asked for two things: name the new dataset after the Legendary Event, not "lre", and make per-unit tuning a generic **horde-mode tier list** (S, A, B, C) rather than an LRE-specific numeric coefficient, since horde-style strength (summoning value, AoE / multi-hit, sustain) applies beyond Legendary Events and a tier is easier to curate than a number.

## What Changes

- **New served dataset `legendary-event-clear-estimate`**: one record with the power ratio and the calibration metadata (sample count, date).
- **New served dataset `horde-mode-tiers`**: one record with the tier multipliers (S, A, B, C in that order) and the characters assigned to a tier. A character not listed is untiered (multiplier 1.0). Seeded with the review's tier list.
- **Raw files** `Data/legendary-event-clear-estimate.json` and `Data/horde-mode-tiers.json`, validated on load, listed in the public manifest with their hashes.
- No calculation endpoint, table or migration; both are served by the existing catalog route. No server-side calculation (ADR 0009). The estimate is not persisted (apps design D2).

Out of scope: the estimate calculation and UI (companion apps change), the anonymised community aggregate (plan §5 "later, separate change", needs a privacy review), renaming the pre-existing `lre-common`, `lres` and `lre-battles` datasets, and who curates the tier list long-term (open question).

## Capabilities

### New Capabilities

- `legendary-event-clear-estimate-dataset`: the served `legendary-event-clear-estimate` record, its fields, defaults and validation.
- `horde-mode-tiers-dataset`: the served `horde-mode-tiers` record, its fields, defaults and validation.

### Modified Capabilities

- None.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-clear-estimates`; apply API first. Shared contract: the two dataset shapes in the specs.
- `src/TacticusPlanner.GameCatalog` (two raw files, model records, loader, a denormalizer for the tier list, two validators, `GameCatalogDatasets` keys, manifest), `src/TacticusPlanner.Api/Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` (routes), the `game-catalog-data` skill (curation notes).
- Tests: `tests/TacticusPlanner.GameCatalog.Tests` (load, validation), `tests/TacticusPlanner.Api.Tests` (served shape, manifest snapshot).
- No infra change. No migration.
- Independent of `add-legendary-event-run-inputs` (Stage 3); either can apply first.
