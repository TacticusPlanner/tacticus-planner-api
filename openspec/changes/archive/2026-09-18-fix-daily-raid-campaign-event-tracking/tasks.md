## 1. Battle-attempt tier tagging

- [x] 1.1 Add `Type` (`string`) to `BattleAttemptRecord` (`src/TacticusPlanner.Domain/PlayerData/Chunks/LiveProgress.cs`), matching `CampaignProgressRecord.Type`'s type and default
- [x] 1.2 Populate it in `MapBattleAttempts` (`src/TacticusPlanner.Api/Features/PlayerData/PlayerDataTransformer.Progress.cs`) from `campaign.Type`, verified by a new/updated unit test in the transformer's test suite asserting a synced response with two same-id, different-`Type` campaign-progress entries (a Standard and an Extremis tier) produces `BattleAttemptRecord`s tagged with the correct `Type` per entry, and that a standing campaign's records also carry their `Type` — `DistinguishesBattleAttemptsFromTwoTiersOfTheSameEventCampaignByType` and updated `PopulatesLiveProgressWithBattleAttemptsAndTheActiveCampaignEventId` in `PlayerDataTransformerTests.cs`, both pass

## 2. Served battle index

- [x] 2.1 Add `BattleIndex` (`int`) to `GameCatalogCampaignBattleView` (`src/TacticusPlanner.GameCatalog/Models/Campaigns.cs`)
- [x] 2.2 Carry `BattleIndex` from `GameCatalogCampaignBattle` into `GameCatalogCampaignBattleView` wherever the view is built (`Denormalization/CampaignDenormalizer.cs` — not `GameCatalogDenormalizer.cs`, which doesn't exist as a standalone file; `GameCatalogDenormalizer` is the partial class split across per-dataset files), verified by new test `ServedBattlesCarryTheirCatalogBattleIndexUnchanged` (`CampaignDenormalizerTests.cs`) asserting a regular node and its same-`nodeNumber` challenge variant carry distinct `battleIndex` values through to the served view. The sequential/per-track-independent assignment itself was already covered by the existing `CatalogCampaignEventBattleIndicesMatchTheUpstreamPerTrackIndices` test (real catalog, `GameCatalogCampaignBattle.BattleIndex`) — this task only needed to prove the view-building step forwards that value rather than dropping or recomputing it, which is what a new assignment-level test would have duplicated
- [x] 2.3 Update `GameCatalogSnapshotTests.cs` (`tests/TacticusPlanner.Api.Tests`) for the new manifest content hash on the affected campaign-battles dataset(s), verified by `dotnet test tests/TacticusPlanner.Api.Tests -c Release --no-build --filter GameCatalogSnapshotTests` — confirmed via `diff` that only the `campaign-battles` dataset hash shifted (all other dataset hashes unchanged), snapshot promoted
- [x] 2.4 Regenerate `artifacts/openapi` and verify `git diff --stat artifacts/openapi` shows only the expected additive field — `battleIndex` on the campaign-battle schema (5 lines). `type` on `BattleAttemptRecord` does not appear in the OpenAPI diff: the player-data chunk endpoint's response type is `PlayerDataChunkEnvelope<object>` (untyped payload — see `GetPlayerDataChunkEndpoint.cs`), so no chunk's internal shape, including this one, is ever reflected in the generated OpenAPI schema; this is pre-existing, unrelated to this change

## 3. Companion change coordination

- [x] 3.1 Confirm the `tacticus-planner-apps` companion change (same change name) updates its `liveProgressSchema`/`RealBattleAttempt` type to accept `type`, and its campaign-battle storage schema/`Battle` type to accept `battleIndex`, before this half ships — confirmed: both fields consumed (`daily-raids-energy.ts`'s unified `buildBattleAttemptIndex`), and the apps repo's full gate suite passed (1525 tests, typecheck, lint, lint:fsd, `git diff --check`)

## 4. Gates

- [x] 4.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
- [x] 4.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` — 0 warnings, 0 errors
- [x] 4.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` — 381/381 passed (127 GameCatalog.Tests + 250 Api.Tests + 4 Persistence.IntegrationTests, GameCatalogSnapshotTests included after promoting the baseline)
