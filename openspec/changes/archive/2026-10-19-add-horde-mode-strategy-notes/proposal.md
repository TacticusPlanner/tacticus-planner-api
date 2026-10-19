## Why

Severyn's tier list revision (2026-10-18) pairs each rating with strategy: strengths, how to use, recommended teammates and relic advice. The text is display content and belongs to the client's locale files, but the recommended teammates are structured data (character ids) that the catalog should serve and validate like the rest of the tier list.

## What Changes

- Each `horde-mode-tiers` character may list `synergies: string[]`: recommended teammates as catalog character ids.
- Seed synergies from Severyn's notes (see `design.md`).
- Validate each synergy id: a catalog character, not the character itself, no duplicates, at most 6.
- Bump `GameCatalogRelease.SchemaVersion` and `Version`.

Depends on **`revise-horde-mode-tier-list`** (archived). It no longer depends on **`add-horde-mode-relic-ratings`**: Severyn chose to build the strategy notes first (2026-10-19), so relic advice is shown as its own block in the Strategy section. Companion change: **`add-horde-mode-strategy-notes`** in `tacticus-planner-apps`, which holds all strategy text in a new locale namespace and shows it on the Tier List. This API half applies first.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `horde-mode-tiers-dataset`: optional per-character synergies and their validation.

## Impact

- `src/TacticusPlanner.GameCatalog`: `Data/horde-mode-tiers.json`, `Models/HordeModeTiers.cs`, `Validation/HordeModeTiersValidation.cs`, `Models/GameCatalogRelease.cs`.
- Tests: `HordeModeTiersTests`, manifest snapshot; OpenAPI artifact regenerates. No EF Core change.
