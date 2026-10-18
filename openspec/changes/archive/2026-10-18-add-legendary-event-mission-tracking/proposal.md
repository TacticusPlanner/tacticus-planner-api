## Why

The web planner wants to suggest Legendary Event battles that move the event's missions forward and to track which missions a run has completed. The catalog serves missions as English free text and the plan stores only a mission count per run, so neither is possible. Companion of `tacticus-planner-apps` change `add-legendary-event-mission-tracking`; this API half applies first.

## What Changes

- The served `lres` dataset carries each regular and premium mission as a typed record: `{ index, kind, count, faction?, alliance?, damageType?, trait?, text }`, parsed from the raw text by a closed set of templates in `LreDenormalizer`. The loader rejects a mission line no template matches, naming the event, file and line.
- Run inputs gain `completedRegularMissions` and `completedPremiumMissions` (distinct catalog indices). The existing counts stay and must be at least the number of ticked missions.
- Bump `GameCatalogRelease.SchemaVersion` and `Version`; update the `game-catalog-data` skill on adding a mission template.
- EF migration for the two new run-input columns.

No **BREAKING** change for clients that ignore the new fields; V2 is pre-production.

## Capabilities

### New Capabilities

- `legendary-event-missions-dataset`: typed missions on the served `lres` records.

### Modified Capabilities

- `legendary-event-plans`: run inputs store completed mission indices.

## Impact

- `TacticusPlanner.GameCatalog`: `Lres.cs` models, `LreDenormalizer`, loader validation, tests over all four raw files.
- `TacticusPlanner.Api/Features/LegendaryEventPlans`: contracts, `LegendaryEventPlanInputMutations`, catalog validator, projection.
- `TacticusPlanner.Persistence`: configuration and migration.
