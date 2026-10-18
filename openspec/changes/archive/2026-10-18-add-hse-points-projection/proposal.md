# Proposal

## Why

The V2 HSE page can only help with the four events that score from campaign raids, and their rules are hard-coded in the client. The catalog's HSE definitions carry no milestones and no scoring rules, so the client cannot show an event's reward ladder, project points for Arena / Onslaught / Salvage Run events (Psyker Boost, Flying Boost, For the Emperor, Defeat Waves, ...) or suggest teams for them. V1 already datamined all of this per event (`tacticusplanner/src/fsd/4-entities/homescreen_events/data/*.json`): milestone ladders per power tier, trackers (points per unit, game modes, enemy and player-unit restrictions) and modifiers (XP multiplier, Arena stat boost).

## What Changes

- New raw dataset `Data/events/home-screen-event-scoring.json`, imported once from the V1 datamine and normalized: per HSE definition, one or more **variants** (Faction Boost per target faction, Defeat Waves 1 and 2, the October 2026 Faction Focus, the global 11th-edition events), each with **milestone tiers**, **trackers** and **modifiers**.
- `EventsDenormalizer` merges each definition's scoring into its served `event-definitions` record as `config.hse` (existing `gameModes` / `statBonus` / `xpBonusTiers` keys are kept). Game-mode ids are normalized to catalog names (`PvP` to `Arena`, `Waves` to `Onslaught`, `TreasureBeach` to `SalvageRun`, `SyncPVP` to `TournamentArena`); reward strings split into id and quantity; `raidBattles` (campaign sweep) trackers, already covered by `killUnits` trackers with equal points, are dropped.
- An HSE occurrence may name its variant with an optional `hseVariant` parameter; validation rejects an unknown variant.
- The `game-catalog-data` skill gains a "refresh HSE scoring from the V1 datamine" step.

Companion apps change: **`add-hse-points-projection`** in `tacticus-planner-apps` (event details, milestones and points projection on Dailies > HSE). Contract surface: the served `event-definitions` dataset's `config.hse`. This API half applies first.

Out of scope: replacing the client's hard-coded raid-point rules with these trackers (kept as is for now, by decision); paid offers (not served).

## Capabilities

### New Capabilities

- `home-screen-event-scoring`: the served `config.hse` shape, variant selection, the raw-to-served derivation and its validation.

### Modified Capabilities

- `game-events-calendar-dataset`: occurrences accept the optional `hseVariant` parameter, validated against the definition's variants.

## Impact

- `src/TacticusPlanner.GameCatalog/Data/events/home-screen-event-scoring.json` (new), `Models/Events.cs` (scoring records), `Denormalization/EventsDenormalizer.cs`, `Validation/EventsValidation.cs`, `event-occurrences.json` (`hseVariant` on the 2026-10-26 Faction Focus).
- Manifest: the `event-definitions` hash changes; no new dataset, so the manifest shape and endpoints are unchanged.
- Tests: `EventsDenormalizerTests`, `EventsValidationTests`, the manifest snapshot.
- No EF Core migration, no endpoint or OpenAPI change.
