## Context

Goal config is a JSON-owned entity (`OwnsOne(c => c.Upgrade, u => u.OwnsMany(u => u.Targets))`
in `GoalConfiguration.cs`). The client's Rank and MoW Ability goals de-duplicate demand per unit by
claiming "slots" (rank slots, ability transitions per track). An Upgrade goal needs the same
progression range to claim comparable slots. Companion apps change: `fix-upgrade-goal-farming-need`.

## Goals / Non-Goals

**Goals:** persist the range an Upgrade goal was created against; keep every existing goal valid;
keep creation and edit validation consistent.

**Non-Goals:** narrowing which upgrade ids are *relevant* by range (server keeps the whole-ladder
relevance rule); backfilling ranges onto existing goals; V1 import of upgrade-material goals.

## Decisions

1. **Shape: three optional nested groups on `UpgradeTarget`.**
   `RankRange? { Start, End }`, `ActiveRange? { Start, End }`, `PassiveRange? { Start, End }`.
   Alternative: one flat set of nullable ints — rejected, an unpaired start/end is representable.
   Active/passive mirror the existing `AbilityTarget` naming; a Character with `activeRange`/
   `passiveRange` (or a MoW with `rankRange`) is rejected.
2. **Per-track optionality.** A MoW may set one track only; the omitted track takes no part in
   overlap. All three absent = legacy behaviour.
3. **Validation.** Rank: `0 <= start < end <= UnitRank.Adamantine3` (same ladder bound Rank goals
   use). Ability tracks: `1 <= start < end` and `end` ≤ the ladder length the catalog defines for
   that track + 1 (a track has one recipe row per transition). Invalid ranges return the same
   400 shape as other target validation. Ranges are independent of `targets` ids (decision not to
   narrow relevance).
4. **Edit replaces the whole upgrade group.** `PUT .../target` with `upgrade: { targets, ranges? }`
   replaces targets *and* ranges. Omitting ranges on edit clears them (explicit, since a partial
   merge would make "remove the range" unrepresentable). `GoalTargetSnapshot` gains the range
   fields so `TargetChanged` events show before/after; `SameTargetAs` includes them.
5. **Migration.** New `OwnsOne` members change the EF model snapshot, so a migration is generated
   in this change. It produces no SQL for existing rows (JSON properties; missing = null) and no
   backfill.

## Risks / Trade-offs

- Server doesn't narrow relevance by range → a client could submit targets outside the range;
  harmless, because the range only drives de-duplication.
- Ability ladder bound depends on catalog data; mis-bounding would reject valid ranges → cover
  with tests over a real MoW (`astraOrdnanceBattery`).

## Migration Plan

Deploy API first (apps change depends on the fields). Old clients that omit ranges keep working.
Rollback = ignore the nullable fields.

## Open Questions

None blocking.
