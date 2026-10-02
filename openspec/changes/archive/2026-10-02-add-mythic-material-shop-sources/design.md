## Context

`GoalConfig.AcquisitionSources` (jsonb, owned collection) already stores `{ kind, ids }` entries
for Unlock/Ascension shard sources. Validation lives in `AcquisitionSourceRules`:
`ShapeError` (catalog-free regex `^<shopId>:(shards|mythicShards)_<unit>$`) and `SemanticError`
(rejects any goal type other than Unlock/Ascension; `Shop` is Character-only and must name a
known shop). Create, combined-create (`CreateCombinedGoalsValidator`), and update
(`GoalEditing.Apply` -> `GoalTargetValidationService.ValidateAcquisitionSources`) all route
through these two functions.

Served shop ids are the raw dataset key minus `shops-` (`guild`, `crusade`, `rogue-trader`,
`war`). The four Mythic upgrade materials appear as plain reward ids (`upgHpM001`..`004`) in
the Guild, Crusade, and Rogue Trader shops.

Companion change: `tacticus-planner-apps` `add-mythic-material-shop-sources`. Shared contract
surface: the existing `acquisitionSources` field on create/update goal requests and the goal
response. No new endpoint, no served-dataset change.

## Goals / Non-Goals

**Goals:**
- Accept and round-trip Mythic-material `Shop` entries on Character Rank, Character/MoW Upgrade,
  and MoW Ability goals. (Character rank-ups reach these materials through Mythic crafted-upgrade
  recipes; MoW ability tracks consume them directly.)
- Keep shard and Mythic-material offer ids from crossing into the wrong goal type.

**Non-Goals:**
- No server-side check that the selected material is actually needed by the goal's range or
  that the offer is currently unlocked (lock state is roster/date dependent and resolved
  client-side, the same as shard offers today).
- No default materialization: `null` stays `null`; the client resolves "all available offers".

## Decisions

1. **Reuse `AcquisitionSources` with kind `Shop`, not a new config field.** The storage, DTOs,
   and OpenAPI shape are already generic. Alternative: a dedicated `mythicMaterialSources`
   field. Rejected: duplicates the shape, needs a contract change, and the client already keys
   shop suppliers by `<shopId>:<rewardType>`.
2. **Gate reward type by goal type in `SemanticError`.** The shape regex widens to
   `(shards_|mythicShards_)<unit>` OR `upgHpM00[1-4]`; `SemanticError` then requires shard
   rewards for Unlock/Ascension and Mythic-material rewards for Rank/Upgrade/MoW-Ability. The four ids live
   in one constant next to `AcquisitionSourceKinds`. Alternative: one regex per goal type in
   `ShapeError`. Rejected: `ShapeError` is goal-type-agnostic by design.
3. **Rank/Upgrade/MoW-Ability accept only `Shop`.** Their campaign override is
   `FarmingLocationIds` (unchanged), and Onslaught never yields upgrade materials. The existing
   "`Shop` is Character-only" rule narrows to Unlock/Ascension (MoW shard offers do not exist);
   Character Ability goals keep rejecting every entry (their costs never include these materials).
4. **Empty Shop ids are a meaningful value.** `[{ Shop, [] }]` is persisted and returned as-is
   (an explicit "no offers"); `null`/`[]` normalizes to `null` as today (the default). No mapper
   change is needed: `MapAcquisitionSources` only collapses an empty outer list.
5. **No EF migration.** The column shape is unchanged; existing rows keep `null`.

## Risks / Trade-offs

- [Reward ids hard-coded] -> A future fifth Mythic material needs a one-line constant change;
  the client has the same list (`MYTHIC_UNCRAFTABLE_UPGRADES`).
- [No relevance check] -> A client could save an offer for a material the range doesn't need;
  harmless (the estimator ignores suppliers for resources with no need).

## Migration Plan

Deploy before the apps companion. Rollback: revert; persisted Rank/Upgrade `Shop` entries would
then fail re-validation only on the next edit of that goal (reads are unaffected).
