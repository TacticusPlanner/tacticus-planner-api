## Why

Today's "Today's Attempts" section and its real-energy-usage indicator silently exclude every raid performed at a campaign-event (Standard/Extremis) node — a player who raided the Adepta Sororitas event campaign today sees those raids nowhere on the page. The `daily-raids-today` spec ("Today's Attempts section", "Today shows real daily energy usage") documents this as a deliberate limitation: the synced `live-progress.battleAttempts` records don't retain which tier (Standard vs Extremis) an event-campaign attempt belongs to, so `{campaignId, battleIndex}` can collide between the two tiers and the apps-side code excludes event campaigns from these views entirely rather than guess.

That limitation is self-inflicted, not a Tacticus API constraint. The raw upstream response already reports Standard and Extremis as two independent `CampaignProgress` entries per event campaign, each carrying its own `Type` and its own `battles[]` (battleIndex-scoped within that entry) — exactly the shape `MapCampaign`/`MapCampaignEvent` (`PlayerDataTransformer.Progress.cs`) already read to build the correctly-tiered `campaign-progress`/`campaign-events-progress` chunks. Only `MapBattleAttempts`, used to build the `live-progress.battleAttempts` chunk, drops `campaign.Type` when flattening every campaign's battles into one list — that's where the tier information is actually lost.

Fixing that alone isn't sufficient for the companion apps change: to turn a `{campaignId, type, battleIndex}` battle-attempt record back into a specific battle (its node number, challenge flag, energy cost), the client needs a `battleIndex` per served battle to key against. The game catalog already computes this internally (`GameCatalogCampaignBattle.BattleIndex`, assigned per `{campaignGroupId, type}` track at catalog-load time — see `GameCatalogLoader.cs`) to build `campaign-events-progress` server-side, but the served battle view (`GameCatalogCampaignBattleView`) never exposes it. Standing campaigns get away without it client-side today because their `nodeNumber - 1` happens to equal their `battleIndex` (no interleaved challenge nodes); an event campaign's challenge nodes share their preceding node's `nodeNumber`, so that shortcut doesn't hold there.

## What Changes

- `BattleAttemptRecord` (`live-progress` chunk) gains a `Type` field, populated from the same `campaign.Type` `MapCampaign`/`MapCampaignEvent` already read — no new upstream data required.
- The served campaign-battle dataset (`GameCatalogCampaignBattleView`) gains a `battleIndex` field, sourced from the catalog's existing internal `GameCatalogCampaignBattle.BattleIndex`.
- No jsonb schema/migration change for the player-data half: `BattleAttempts` is already an owned-JSON collection inside the `live_progress` column (`ToJson`), so adding a field to the nested record changes only the serialized shape, not the table structure.
- **BREAKING** (internal contract, not the public API surface): the served `live-progress.battleAttempts[]` item shape gains a required `type` field, and the served campaign-battle shape gains a required `battleIndex` field. Both are additive to their JSON shapes, but the companion apps change's schema treats them as a compatibility boundary the same way `expectedGold` was in the prior paired change.

## Capabilities

### Modified Capabilities

- `player-data-sync`: gains two requirements — battle-attempt records preserve the campaign type/tier they were reported under (mirroring `campaign-progress`/`campaign-events-progress`), and served campaign battles carry their Tacticus battle index — so a consumer can resolve `{campaignGroupId, type, battleIndex}` to a specific battle for any campaign, event campaigns included.

## Impact

- `src/TacticusPlanner.Domain/PlayerData/Chunks/LiveProgress.cs` (`BattleAttemptRecord`): add `Type`.
- `src/TacticusPlanner.Api/Features/PlayerData/PlayerDataTransformer.Progress.cs` (`MapBattleAttempts`): populate `Type` from `campaign.Type`.
- `src/TacticusPlanner.GameCatalog/Models/Campaigns.cs` (`GameCatalogCampaignBattleView`): add `BattleIndex`.
- `src/TacticusPlanner.GameCatalog/Denormalization/GameCatalogDenormalizer.cs` (wherever `GameCatalogCampaignBattleView` is built from `GameCatalogCampaignBattle`): carry `BattleIndex` through.
- Served `live-progress` chunk payload/DTO and campaign-battles dataset payload/DTO, plus `artifacts/openapi`: additive fields.
- Companion apps change: `tacticus-planner-apps` (same change name) — consumes `type` and `battleIndex` to stop excluding event-campaign raids from Today's Attempts, real energy usage, and per-node attempts-left.
