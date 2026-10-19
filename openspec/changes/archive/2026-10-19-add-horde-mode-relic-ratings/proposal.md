## Why

Some characters are much stronger in horde-style modes once they hold a specific Mythic relic (Severyn's tier list revision, 2026-10-18). Raising their base multiplier would overrate every player who lacks the relic, so the relic's effect is kept conditional: a separate, higher multiplier that applies only when the relic is equipped.

## What Changes

- Each `horde-mode-tiers` character may list `relics: { equipmentId, multiplier }[]`: a catalog relic and the multiplier the character gets while it is equipped.
- Seed eight relic ratings (see `design.md`): Re'vas, Maladus, Angrax, Snotflogga, Anuphet, Nicodemus, Typhus and Abaddon.
- Validate that each `equipmentId` is a catalog relic the character may equip, listed once per character, with a multiplier in (0, 5] and not below the character's base effective multiplier.
- Bump `GameCatalogRelease.SchemaVersion` (the served shape gains a field) and `Version`.

Depends on **`revise-horde-mode-tier-list`** (archived). Built in the same PR as **`add-horde-mode-strategy-notes`** (Severyn, 2026-10-19), after it. Companion change: **`add-horde-mode-relic-ratings`** in `tacticus-planner-apps`, which uses the relic multiplier in clear estimates when the synced roster has the relic equipped and shows it on the Tier List. This API half applies first.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `horde-mode-tiers-dataset`: optional per-character relic ratings and their validation.

## Impact

- `src/TacticusPlanner.GameCatalog`: `Data/horde-mode-tiers.json`, `Models/HordeModeTiers.cs` (new `GameCatalogHordeModeRelic`), `Validation/HordeModeTiersValidation.cs` (needs the equipment ids and their allowed units), `Models/GameCatalogRelease.cs`.
- Tests: `HordeModeTiersTests`, manifest snapshot. The OpenAPI artifact regenerates because the served model gains a field. No EF Core change.
