# Tasks

Companion apps change: `add-hse-points-projection` in `tacticus-planner-apps` (applies after this one). Contract: `event-definitions[].config.hse`.

## 1. Data

- [ ] 1.1 Write the one-off import (design D3, D4) from `tacticusplanner/src/fsd/4-entities/homescreen_events/data/*.json` and commit its output as `Data/events/home-screen-event-scoring.json`; verify the Psyker Boost, Faction Boost, Operation Imperator and Machine Hunt entries by hand against the scenarios.
- [ ] 1.2 Add `hseVariant: "october-2026"` to the 2026-10-26 `hse-faction-focus` occurrence in `event-occurrences.json`.
- [ ] 1.3 Document the refresh step in the `game-catalog-data` skill.

## 2. Model, denormalization, validation

- [ ] 2.1 Add the scoring records to `Models/Events.cs` and load the raw file in `GameCatalogLoader`.
- [ ] 2.2 Merge scoring into served definitions as `config.hse` in `EventsDenormalizer`, keeping existing config keys; verify `EventsDenormalizerTests` covers Psyker Boost (every field of the first scenario), Faction Boost variants, a global variant, dropped `raidBattles`, and Against the Tide without `config.hse`.
- [ ] 2.3 Add the D5 checks and `hseVariant` validation to `EventsValidation`; verify `EventsValidationTests` covers each failure and the optional parameter passing.

## 3. Manifest and gates

- [ ] 3.1 Update the catalog manifest snapshot (the `event-definitions` hash changes); confirm no endpoint or OpenAPI change.
- [ ] 3.2 Run the repo's build, format and test gates.
