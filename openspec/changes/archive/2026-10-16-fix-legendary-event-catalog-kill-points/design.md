# Design

## Context

A raw lane in `Data/lres/lres-*.json` carries `killPoints` (a constant) and `defeatAll` (one value per battle: 30 for 17 battles then 36 for Uthar Alpha, for example). Both come from V1's hand-authored event files. The loader validates the reward ladder (`ValidateLreRewards`) and, with `fix-legendary-event-run-and-goal`, the run starts; nothing relates `killPoints` to `defeatAll`. The clients read `killPoints` as the lane's per-battle defeat-all base in unit points per battle (leaderboard, master table, team cards, suggestions, upgrade impact) and `defeatAll[i]` in the per-battle progress and token plan. Measured on 2026-10-16: for `astarLysander`, `lostaBile` and `tauFarsight` every lane has `killPoints == defeatAll[0]`; for `votanUthar` the three `killPoints` (34, 26, 17) equal `tauFarsight`'s while `defeatAll[0]` is 30, 34, 43, and V1's Uthar page shows Defeat All 30, 34, 43. Companion apps change: same name in `tacticus-planner-apps`.

## Goals / Non-Goals

**Goals:**

- Correct Uthar's three values.
- Make the next copy error fail the load instead of the leaderboard.

**Non-Goals:**

- Removing or renaming `killPoints` (clients read it widely; a rename is a contract change for its own change, and the name misleads: it is the defeat-all base, not the kill score).
- Using `defeatAll[i]` per battle in per-unit points (the leaderboard base stays a constant, as V1 shows it).
- Validating `battlesPoints` or objective scores against any other source.

## Decisions

**D1. Fix the data and add a consistency rule, keep the field.** The raw files mirror V1's format and the refresh process in the `game-catalog-data` skill copies fields across, so a derived or removed field would diverge from that process. A validator rule is cheap and catches this class of error. Alternative: derive the served `killPoints` from `defeatAll[0]` and ignore the raw field. Rejected: it would hide a wrong raw value instead of surfacing it.

**D2. The rule is equality with `defeatAll[0]`.** Later `defeatAll` values legitimately grow (Uthar Alpha reaches 36 at battle 18), so only the first element is compared. An empty `defeatAll` is invalid.

**D3. Validation lives beside the other LRE checks.** A `ValidateLreLanePoints` next to `ValidateLreRewards`, same error shape, called from `GameCatalogValidator`, so the loader rejects it with the same mechanism and tests.

## Risks / Trade-offs

- [A future event whose game data genuinely has a constant that differs from `defeatAll[0]`] → The loader fails loudly and the maintainer decides; the rule is easy to relax per lane if the game ever does that.
- [Uthar's leaderboard and master table figures change for every user] → Intended: they now match V1 and the game (Dante's Uthar cell 4,608 → 5,004). The apps half updates the worked examples that used the old values.
- [`killPoints` stays a misleading name] → Documented in the spec and the skill; renaming is a separate contract change.

## Migration Plan

No migration. Deploy this half first; the dataset hash changes so clients re-download `lres` on their next manifest check. Roll back by reverting the three numbers and the validator.
