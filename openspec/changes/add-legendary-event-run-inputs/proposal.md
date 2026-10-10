# Proposal

## Why

Stage 3 of the V2 Legendary Event plan ("Economy inputs and reward projection", docs plan §5) gives the event page a forward-looking answer: how many more points until the next ascension step of the event unit. That projection needs two things the API does not provide today.

1. **Run inputs the Tacticus API cannot see.** Missions completed, premium missions, the 300-currency bundle and "Oh So Close" shards are per run (1–3) and are the only manual economy inputs the plan keeps (plan §3, pain #9). V1 stores them in `leProgress[*].overview`; V2 has nowhere to put them.
2. **A correct reward ladder per event.** The catalog serves the points/chest ladder once, as `lre-common`, taken from the alphabetically first event, on the assumption that it is the same for every event (plan §8 risk). It is not: checked on 2026-10-10, `astarLysander` and `lostAbile` have 60 chest rungs (total 44,450 currency) while `tauFarsight` and `votanUthar` have 54 (total 35,450). The points ladder, progression and shards per chest match today, but the chest ladder does not, so any projection for Farsight or Uthar built on `lre-common` overstates the chests available.

## What Changes

- **Run inputs on the plan.** New table `legendary_event_run_progress` (plan × run 1–3: `regular_missions`, `premium_missions`, `bundle_purchased`, `close_shards`), served on the plan as `runs[]`, written by a new `PUT /me/legendary-event-plans/{eventId}/runs/{run}` that follows the plan's single-revision contract. Values are validated against the event's catalog mission counts. Migration included.
- **Per-event reward ladder in the catalog.** Each served `lres` event gains a `rewards` object (`pointsMilestones`, `chestsMilestones`, `progression`, `shardsPerChest`) denormalized from its own raw file. The `lre-common` dataset is **removed** (V2 is pre-production; breaking catalog changes are allowed). Manifest, snapshot and denormalization tests updated.
- **V1 import extended.** The `legendaryEventPlans` part also imports `leProgress[*].overview[1..3]` into run rows and `leSettings.showP2POptions` into `show_paid_options` on plans the import creates. Run inputs are imported even when the plan already has teams (so users who imported teams before this change can still bring their inputs), and never overwrite a V2 run row. V1 `MaybeClear` / `StopHere` statuses stay unimported (see design D6).
- **OpenAPI artifact** regenerated.

Out of scope: the projection itself (client side, ADR 0009), notes UI, token planning (Stage 4), clear-depth estimates (Stage 5, `add-legendary-event-clear-estimates`), plan listing (Stage 7).

## Capabilities

### New Capabilities

- `legendary-event-reward-ladder`: the per-event reward ladder served on each `lres` event, its derivation from the raw event file, and the removal of `lre-common`.

### Modified Capabilities

- `legendary-event-plans`: the plan carries run inputs per run; a run write endpoint; the served shape gains `runs`.
- `v1-legendary-event-import`: the part reads `overview` and `showP2POptions`, iterates events found in either the teams or the progress key, and imports run inputs independently of teams.

## Impact

- Companion apps change: `tacticus-planner-apps/openspec/changes/add-legendary-event-run-inputs`; apply API first. Shared contracts: `LegendaryEventPlanResponse.runs`, `PUT …/runs/{run}`, `lres[].rewards`, the removal of `lre-common`, and the extended `legendaryEventOutcomes` (`runInputsImported`, new codes).
- `src/TacticusPlanner.Domain/LegendaryEvents/` (new `LegendaryEventRunProgress`), `src/TacticusPlanner.Persistence` (configuration, query filter, one migration), `src/TacticusPlanner.Api/Features/LegendaryEventPlans/*` (run endpoint, projection, validator), `src/TacticusPlanner.Api/Features/V1Import/*` (`TacticusV1Client` records, `V1LegendaryEventImportService`), `src/TacticusPlanner.GameCatalog` (`Models/Lres.cs`, `Denormalization/LreDenormalizer.cs`, `GameCatalogDatasets`, loader, manifest validation), `src/TacticusPlanner.Api/Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs`, `artifacts/openapi/TacticusPlanner.Api.json`.
- Tests: `tests/TacticusPlanner.Api.Tests` (run endpoint, validation, import), `tests/TacticusPlanner.GameCatalog.Tests` (per-event rewards, manifest snapshot), `tests/TacticusPlanner.Persistence.IntegrationTests` (migration, constraints, cascade).
- No infra change.
- Docs: `architecture/data/events.md` (`legendary_event_run_progress` moves from proposal to implemented; `lre-common` note), the feature spec's open question on `MaybeClear` / `StopHere` (resolved as dropped, pending Severyn's confirmation), and plan §8's ladder risk (confirmed and fixed here).
