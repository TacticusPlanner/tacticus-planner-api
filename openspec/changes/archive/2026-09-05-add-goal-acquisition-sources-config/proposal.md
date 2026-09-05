## Why

The `tacticus-planner-apps` change `add-shop-shard-acquisition-sources` redesigns the
Unlock/Ascension shard-source picker into a multi-select of Campaigns, Onslaught, and daily
Shops that can be combined in any mix. The server-owned goal config only models a single
`AscensionFarmingSource` enum (`Campaign` / `Onslaught` / `Both`) plus campaign battle-id
lists, so it cannot persist a multi-source selection, shop-offer choices, or a shape that a
later `Incursion` source (TacticusPlanner/tacticus-planner-apps#106) can extend without a
second break. This change extends the goal target model to carry an open, growable
acquisition-source set.

## What Changes

- **BREAKING** (V2 is pre-production; allowed per `tp-destructive-changes-policy`): replace
  `GoalConfig.AscensionFarming` and the Unlock/shard-farming role of
  `GoalConfig.FarmingLocationIds` with `GoalConfig.AcquisitionSources` — an ordered list of
  `{ kind, ids }` entries.
  - `kind`: a server-validated allow-list — `Campaign`, `Onslaught`, `Shop` — designed to
    grow. `Incursion` is reserved for #106 and is **not** added here.
  - `ids` per kind: `Campaign` → campaign shard battle ids (regular and mythic together, the
    client re-splits); `Shop` → shop-offer ids of the form `<shopId>:<rewardType>`;
    `Onslaught` → empty.
- `GoalConfig.FarmingLocationIds` is **retained** for the Rank/Ability upgrade-node override;
  only its Unlock shard-node role moves into `AcquisitionSources`.
- Request/response surface: `CreateGoalConfigRequest` gains `AcquisitionSources` and drops
  `AscensionFarming`; `AscensionFarmingRequest` is removed; the combined-create path
  (`CreateCombinedGoalsRequest`) carries it per goal; `UpdateGoalRequest` gains
  acquisition-source editing; `GoalMapper` maps both directions; `GoalConfigResponse` /
  `AscensionFarmingResponse` updated. OpenAPI is regenerated.
- Validation: reject an unknown `kind`; `Campaign` ids must be valid shard battle ids for the
  target character (reuse `GoalTargetValidationService`'s regular/mythic sets); `Shop` ids
  must match the `<shopId>:<rewardType>` shape and reference a real shop; `Onslaught` entries
  carry no ids and are accepted only for Character Ascension goals; `Onslaught` and `Shop`
  entries are rejected for MoW goals.
- Persistence: `GoalConfiguration` maps `config.OwnsMany(c => c.AcquisitionSources)` in the
  existing `config` jsonb column and drops `OwnsOne(c => c.AscensionFarming)`; a schema-
  neutral EF migration records the model change.
- **Data migration**: rewrite each goal's `config` jsonb — synthesize `acquisitionSources`
  from the old fields (`Campaign` from `AscensionFarming.ShardBattleIds ∪
  MythicShardBattleIds`, or from an `Unlock` goal's `FarmingLocationIds`; add `Onslaught`
  when `AscensionFarming.Source ∈ { Onslaught, Both }`), then clear the Unlock
  `FarmingLocationIds` and remove `ascensionFarming`. A goal with neither old field becomes
  `[{ kind: "Campaign", ids: [] }]` (unrestricted campaign — today's default behaviour).
- `V1GoalImportService` maps imported V1 farming choices into `AcquisitionSources`.
- Coordinated with the apps change: the hand-maintained client goal types in
  `tacticus-planner-apps` are updated in the same release — no dual-read/transition window.

## Capabilities

### Modified Capabilities

- `goal-target-model`: add the acquisition-source config model — the `{ kind, ids }` set, the
  growable `kind` allow-list, the per-kind `ids` rules and validation, the removal of the
  `AscensionFarming` enum model, and the migration of existing goals.

## Impact

- **Domain:** `src/TacticusPlanner.Domain/Goals/GoalConfig.cs` (`AcquisitionSource` type +
  list; remove `AscensionFarmingConfig` / `AscensionFarmingSource`).
- **Persistence:** `src/TacticusPlanner.Persistence/Configurations/GoalConfiguration.cs`; a
  new migration under `src/TacticusPlanner.Persistence/Migrations/` (model snapshot + jsonb
  data rewrite).
- **API:** `Features/Goals/CreateGoalEndpoint.cs`, `CreateCombinedGoalsEndpoint.cs`,
  `UpdateGoalEndpoint.cs`, `GoalMapper.cs`, `CreateGoalValidator.cs`,
  `GoalTargetValidationService.cs`; generated OpenAPI.
- **V1 import:** `src/TacticusPlanner.Api/Features/V1Import/V1GoalImportService.cs`.
- **Consumers:** `tacticus-planner-apps` (`add-shop-shard-acquisition-sources`) — hand-written
  types in `apps/web/src/fsd/entities/goal/model/types.ts` and the goal-spec builder must
  land together.
- **Depends on / paired with:** `tacticus-planner-apps#103` and its OpenSpec change
  `add-shop-shard-acquisition-sources`. Forward-compatible with `tacticus-planner-apps#106`
  (Incursion).
