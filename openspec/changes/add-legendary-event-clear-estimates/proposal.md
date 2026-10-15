# Proposal

## Why

Stage 5 of the V2 Legendary Event plan ("Clear-depth estimation", docs plan §5; survey pain #2, "users must guess how far each team gets"). The apps change estimates how many battles a team, or a single unit, clears on a lane from combat power, per-character horde-mode strength and a power ratio. The Stage 1 follow-up (2026-10-07) decided per-unit tuning is **curated catalog data**, so it never needs a client release.

Review (2026-10-15) shaped it further: per-unit tuning is a generic **horde-mode tier list** (S, A, B, C) rather than a Legendary Event-specific numeric coefficient, since horde-style strength (summoning value, AoE / multi-hit, sustain) applies beyond Legendary Events and a tier is easier to curate than a number; a character may still carry its own multiplier to fine-tune within a tier. The power ratio is a single Legendary Event-specific constant, so it lives in the apps code rather than in a catalog dataset of its own.

## What Changes

- **New served dataset `horde-mode-tiers`**: one record with the tier multipliers (S, A, B, C in that order) and the characters assigned to a tier, each with an optional `multiplier` override. A character not listed is untiered (multiplier 1.0). Seeded with the review's tier list.
- **Raw file** `Data/horde-mode-tiers.json`, validated on load, listed in the public manifest with its hash.
- No calculation endpoint, table or migration; the dataset is served by the existing catalog route `GET /api/v1/game-catalog/horde-mode-tiers`. No server-side calculation (ADR 0009). The estimate is not persisted (apps design D2).

Out of scope: the estimate calculation, the power ratio and the UI (companion apps change), the anonymised community aggregate (plan §5 "later, separate change", needs a privacy review), renaming the pre-existing `lre-common`, `lres` and `lre-battles` datasets, and who curates the tier list long-term (open question).

## Capabilities

### New Capabilities

- `horde-mode-tiers-dataset`: the served `horde-mode-tiers` record, its fields, defaults and validation.

### Modified Capabilities

- None.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-clear-estimates`; apply API first. Shared contract: the `horde-mode-tiers` dataset shape in the spec.
- `src/TacticusPlanner.GameCatalog` (raw file, model records, loader, a denormalizer, a validator, `GameCatalogDatasets` key, manifest), `src/TacticusPlanner.Api/Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` (route), the `game-catalog-data` skill (curation notes).
- Tests: `tests/TacticusPlanner.GameCatalog.Tests` (load, validation, serialization), `tests/TacticusPlanner.Api.Tests` (served shape, manifest snapshot).
- No infra change. No migration.
- Independent of `add-legendary-event-run-inputs` (Stage 3); either can apply first.
