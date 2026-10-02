## Why

Rank goals that reach Adamantine need the four Mythic upgrade materials (`upgHpM001`–`upgHpM004`,
e.g. Venerable Battle Mark), and Machine-of-War ability upgrades consume them directly. These are
only reliably obtainable from daily shops (Guild, Crusade, Rogue Trader), so the planner reports
them as blocked and gives such goals no completion date. The companion apps change lets Rank,
Upgrade, and Machine-of-War Ability goals select those shop offers as acquisition sources; the API
must accept and persist that selection.

## What Changes

- Accept `Shop` acquisition-source entries on Character **Rank**, Character and Machine-of-War
  **Upgrade**, and Machine-of-War **Ability** goals, whose ids name a shop offer of one of the four
  Mythic upgrade materials (`<shopId>:upgHpM00[1-4]`).
- Extend the shop-offer id shape check to accept those four Mythic material reward types alongside
  the existing `shards_*` / `mythicShards_*` reward types.
- Gate reward types by goal type: Unlock/Ascension `Shop` ids must be shard rewards;
  Rank/Upgrade/MoW-Ability `Shop` ids must be Mythic-material rewards. `Campaign` and `Onslaught`
  entries stay rejected on Rank/Upgrade/Ability goals (their campaign override remains
  `farmingLocationIds`).
- Persist these `acquisitionSources` unchanged in the existing jsonb config; `null` keeps meaning
  "use the client default" (all available offers, resolved client-side), and an entry
  `{ kind: "Shop", ids: [] }` round-trips as an explicit "no shop offers" choice.
- No schema change, no migration, no catalog change; the OpenAPI shape is unchanged.

Companion change: `tacticus-planner-apps` — `add-mythic-material-shop-sources` (picker UI,
estimator, shared shop capacity in the Plan). This API half applies first.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `goal-target-model`: acquisition-source id validation and kind/goal-type gating extend to Mythic
  material shop offers on Character Rank, Character/MoW Upgrade, and MoW Ability goals.

## Impact

- `src/TacticusPlanner.Api/Features/Goals/AcquisitionSourceRules.cs` (shape regex, semantic gating
  by goal type, entity type, and reward type).
- `src/TacticusPlanner.Domain/Goals/AcquisitionSource.cs` / `GoalConfig.cs` doc comments (no longer
  Unlock/Ascension-only).
- Create, combined-create, and update endpoints pick the new rules up through the shared
  validators; endpoint integration tests extended.
- No EF migration, no catalog data change, no OpenAPI contract change.
