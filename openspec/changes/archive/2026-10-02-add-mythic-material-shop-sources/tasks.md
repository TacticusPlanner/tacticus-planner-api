## 1. Validation

- [x] 1.1 Add the four Mythic material reward ids (`upgHpM001`..`upgHpM004`) as a constant beside `AcquisitionSourceKinds` and widen `AcquisitionSourceRules.ShapeError`'s shop-offer regex to accept `<shopId>:upgHpM00[1-4]`; verify with unit tests that `guild:upgHpM004` passes and `guild:upgDmgL202` fails the shape check.
- [x] 1.2 Update `AcquisitionSourceRules.SemanticError` gating: Unlock/Ascension accept Campaign/Onslaught/Shop with shard-reward shop ids only; Character Rank, Character/MoW Upgrade and MoW Ability accept `Shop` with Mythic-material ids only (empty `ids` allowed); Campaign/Onslaught rejected on those; MoW Unlock/Ascension reject `Shop`; Character Ability rejects every entry. Verify with unit tests covering each spec scenario.
- [x] 1.3 Update the `GoalConfig.AcquisitionSources` / `AcquisitionSource` doc comments to describe Rank/Upgrade Mythic-material shop sources; verify `dotnet build` has no XML-doc warnings.

## 2. Endpoint coverage

- [x] 2.1 Extend `GoalsEndpointTests` with: Rank goal create with `[{ Shop, ["guild:upgHpM004","crusade:upgHpM004"] }]` plus `farmingLocationIds` round-trips both; Rank goal with `[{ Shop, [] }]` round-trips distinct from `null`; shard offer on Rank and Mythic offer on Ascension are rejected (400).
- [x] 2.2 Extend `UpgradeGoalsEndpointTests` / `GoalEditEndpointTests` with an Upgrade goal update to `[{ Shop, ["rogue-trader:upgHpM004"] }]` that round-trips, a MoW Upgrade goal Mythic Shop entry that round-trips, a MoW Ability goal (`ultraDreadnought`) Mythic Shop entry that round-trips, and a Character Ability goal Shop entry that is rejected.
- [x] 2.3 Extend `CreateCombinedGoalsEndpointTests` with a combined Rank goal carrying a Mythic-material Shop entry; verify it persists.
- [x] 2.4 Build and confirm the regenerated `artifacts/openapi/TacticusPlanner.Api.json` has no diff (contract unchanged); note the companion `tacticus-planner-apps` change `add-mythic-material-shop-sources` consumes the same field.

## 3. Gates

- [x] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, and `dotnet test TacticusPlanner.slnx -c Release --no-build`; all pass.
