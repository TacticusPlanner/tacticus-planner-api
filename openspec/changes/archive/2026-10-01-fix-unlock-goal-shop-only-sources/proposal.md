## Why

The API rejects Unlock goals for characters whose only shard source is a shop. `GameCatalogGoalLookups.IsUnlockEligible` requires `ShardLocations.Count > 0` (campaign nodes only), so Kharn and Ragnar — sold as shards in the Guild War shop (and Rogue Trader) but with no campaign nodes — get `400 "Unlock is unavailable because the catalog has no shard-upgrade data for this character."` even though the client (companion change) now offers the goal. Reproduced against local dev.

## What Changes

- `IsUnlockEligible` returns true for a Character with at least one campaign shard-farm node **or** at least one regular (non-mythic) shard variant in any catalog shop (`GameCatalogShopVariantView.UnitId == id` and `Reward.Type == "shards_<id>"`).
- The rejection message is reworded to name both sources.
- No request/response contract, OpenAPI, or persistence change.

Companion apps change: `fix-unlock-goal-shop-only-sources` (tacticus-planner-apps). This half applies first.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `goal-target-model`: Unlock eligibility for a Character now includes shop shard offers, not only campaign shard nodes.

## Impact

- `src/TacticusPlanner.GameCatalog/GameCatalogGoalLookups.cs` (eligibility) and `src/TacticusPlanner.Api/Features/Goals/GoalTargetValidationService.cs` (message).
- API tests for create-goal and the lookup.
- No migrations, no OpenAPI change.
