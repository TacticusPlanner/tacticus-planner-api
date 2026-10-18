## Why

The horde-mode tier list seeded with the Legendary Event clear estimates has 21 characters in four tiers (S, A, B, C). Severyn's first balancing revision re-rates those 21 and adds 26 characters that matter in horde-style modes (Legendary Events, Survival, Onslaught) but are missing today, among them Tyrant Guard, Snotflogga, Jaeger and Tyrith. It also adds a D tier for characters that have been evaluated and found weak, so "untiered" keeps meaning "not yet evaluated". D uses ×1.00, the same as untiered, so no character gets a penalty. The values are balancing estimates, not measured clear rates; they are to be calibrated against real clears later.

## What Changes

- Tiers become **S, A, B, C, D** with defaults ×1.90, ×1.70, ×1.45, ×1.20, ×1.00 (from ×1.85, ×1.55, ×1.25, ×1.05).
- `horde-mode-tiers.json` lists 47 characters (from 21), every one with its own `multiplier`. The full list is in `design.md`. Notable moves: Nauseous A→S, Maladus C→S, Re'vas A→S, Angrax C→A, Gulgortz B→A, Burchard B→A, Bellator S→A, Sho'syl S→A, Certus C→D.
- The validator accepts exactly S, A, B, C, D in that order. The per-tier ranges (S 1.80–2.00, A 1.60–1.79, B 1.35–1.59, C 1.10–1.34, D 1.00–1.09) are authoring guidance and are not validated, so a deliberate outlier stays possible.
- `GameCatalogRelease.Version` is date-based and already today's; `SchemaVersion` stays: the served shape is unchanged.

Out of scope here, proposed as follow-up changes on the same branch: relic-conditional multipliers (`add-horde-mode-relic-ratings`) and per-character strategy (`add-horde-mode-strategy-notes`).

Companion change: **`revise-horde-mode-tier-list`** in `tacticus-planner-apps`. It gives the D tier its own colour on the Tier List and team-picker tiles and updates the Tier List spec. This API half applies first.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `horde-mode-tiers-dataset`: tiers are S, A, B, C, D; the seed list is the revised 47 characters.

## Impact

- `src/TacticusPlanner.GameCatalog`: `Data/horde-mode-tiers.json`, `Validation/HordeModeTiersValidation.cs` (`HordeModeTierNames`).
- `tests/TacticusPlanner.GameCatalog.Tests/HordeModeTiersTests.cs` (seed and tier assertions, the unknown-tier case moves from D to E) and the manifest snapshot (hash changes).
- No endpoint, contract, OpenAPI or EF Core change.
