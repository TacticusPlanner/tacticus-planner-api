## 1. Denormalization

- [x] 1.1 Add an `expectedGold` field to `GameCatalogFarmLocation` (`src/TacticusPlanner.GameCatalog/Models/Units.cs`), typed `double?` — not `decimal?` as originally noted: matches the sibling `EffectiveRate double?` field on the same record, and the served JSON wire type (`["null","number"]`, confirmed in the OpenAPI diff) is identical either way, so this is a type-consistency correction with no behavior change
- [x] 1.2 In `GameCatalogDenormalizer.BuildRewardLocations` (`Denormalization/GameCatalogDenormalizer.cs`), build a `battleId -> double? expectedGold` lookup from each battle's `Rewards.Guaranteed` entries where `Id == "gold"`, computed as `(min + max) / 2.0`, alongside the existing reward-location index
- [x] 1.3 Thread that lookup into `ResolveLocations` and stamp `expectedGold` onto every returned `GameCatalogFarmLocation` (both the single-location and the consolidated/grouped branch), verified by a new unit test in `tests/TacticusPlanner.GameCatalog.Tests/UpgradeDenormalizerTests.cs` (new file, following the pattern of `ShopsDenormalizerTests.cs`) asserting: two battles tying on drop rate/energy report their own distinct `expectedGold`, a battle with no guaranteed gold reward reports null, and two different resources dropped by the same battle report the same `expectedGold` — all 3 tests pass

## 2. Served schema and contract

- [x] 2.1 Add `expectedGold` to the served dataset payload schema/DTO for upgrade and character-shard farm locations and regenerate `artifacts/openapi`, verified by `git diff --stat artifacts/openapi` showing only the expected additive field — confirmed: 9 lines changed, purely the new `expectedGold: ["null","number"]` property added to the farm-location schema, no other field touched
- [x] 2.2 Update `GameCatalogSnapshotTests.cs` (`tests/TacticusPlanner.Api.Tests`) for the new manifest content hash and field, verified by `dotnet test tests/TacticusPlanner.Api.Tests -c Release --no-build --filter GameCatalogSnapshotTests` — the test source needed no code change; its `.verified.txt` snapshot baseline was regenerated and promoted (only the `characters` and `upgrades` dataset hashes shifted, as expected — no other dataset's hash moved)

## 3. Companion change coordination

- [x] 3.1 Confirm the `tacticus-planner-apps` companion change (same change name) updates its `datasetPayloadSchemas` zod schema and `FarmLocation`/`shared/lib/battle.domain.ts` type to accept `expectedGold` before this half ships — confirmed: `farmLocationSchema` (`packages/game-catalog/src/schemas/shared.ts`) now has `expectedGold: z.number().nullable().optional()`, and `FarmLocation` (`apps/web/src/fsd/shared/lib/battle.domain.ts`) has `expectedGold?: number | null`; both applied and the apps repo's full gate suite (test/typecheck/lint/lint:fsd) passes

## 4. Gates

- [x] 4.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` — no changes
- [x] 4.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` — 0 warnings, 0 errors
- [x] 4.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` — 379/379 passed
