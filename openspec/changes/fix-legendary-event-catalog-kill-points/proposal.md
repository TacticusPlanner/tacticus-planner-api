# Proposal

API half of the paired change `fix-legendary-event-catalog-kill-points` (companion: `tacticus-planner-apps`, same name). This half applies first. Independent of the run-schedule change (`fix-legendary-event-run-and-goal`), though both edit `lres` raw files, so apply them one after the other.

## Why

Each lane of a served Legendary Event carries `killPoints`, the points a won battle's defeat-all base adds in the leaderboard and master table. For every event but one it equals the lane's first `defeatAll` value. For `votanUthar` it does not: the raw file has `killPoints` 34 / 26 / 17 (Alpha / Beta / Gamma) while its `defeatAll` arrays start at 30 / 34 / 43, and V1 and the in-game lane screens show Defeat All 30 / 34 / 43. The 34 / 26 / 17 are exactly `taufarsight`'s values, so they were copied from the wrong file. Uthar's per-unit points per battle (leaderboard, master table, team cards, suggestions, upgrade impact) therefore use the wrong base on every lane (for example Alpha 34 instead of 30, Gamma 17 instead of 43). Nothing in the loader notices because no rule ties the two fields together.

## What Changes

- Correct `lres-votanuthar.json`: `killPoints` 30 (Alpha), 34 (Beta), 43 (Gamma).
- The catalog loader rejects a raw lane whose `killPoints` differs from its first `defeatAll` value, naming the event, lane and both values, so a copy error cannot ship again.
- No served shape change, no migration; the served `killPoints` keeps its meaning (the defeat-all base of the first battle), only Uthar's values change.

## Capabilities

### New Capabilities

- `legendary-event-lane-points`: what a served lane's `killPoints` means and the consistency rule the loader enforces between it and `defeatAll`.

### Modified Capabilities

None.

## Impact

- `src/TacticusPlanner.GameCatalog/Data/lres/lres-votanuthar.json` (three numbers), `Validation` (new lane-points check next to the reward ladder and run-start checks), `GameCatalogValidator`.
- Tests: `tests/TacticusPlanner.GameCatalog.Tests` (valid events pass, a mismatched lane fails with the named error) and the manifest snapshot (the `lres` dataset hash changes).
- Companion apps change: refreshes the Uthar test fixture and recomputes the master table's Uthar worked examples; no apps logic changes.
- Release metadata: content-only change; `SchemaVersion` stays.
