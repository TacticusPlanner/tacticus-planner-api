# Tasks

Companion to `tacticus-planner-apps` change `add-shop-shard-acquisition-sources` — must land
in the same release. Spec: `specs/goal-target-model/spec.md`. Design: `design.md` DA1–DA7.

## 1. Domain model

- [x] 1.1 Add `sealed class AcquisitionSource { string Kind; List<string> Ids = []; }` and
  `List<AcquisitionSource>? AcquisitionSources` to `GoalConfig`
  (`src/TacticusPlanner.Domain/Goals/GoalConfig.cs`); remove `AscensionFarmingConfig` and
  `AscensionFarmingSource`. Verify: `dotnet build TacticusPlanner.slnx -c Release` fails only
  where the removed types are still referenced (worked through in later tasks).
- [x] 1.2 Add a server constant `AcquisitionSourceKinds` (`Campaign`, `Onslaught`, `Shop`)
  with a doc comment noting it is intentionally growable (`Incursion` → apps#106). Verify:
  referenced by both validators (tasks 4.x).

## 2. Persistence

- [x] 2.1 In `GoalConfiguration.cs` replace `config.OwnsOne(c => c.AscensionFarming)` with
  `config.OwnsMany(c => c.AcquisitionSources)`. Verify: `dotnet build` of
  `TacticusPlanner.Persistence` succeeds.
- [x] 2.2 Generate the EF migration (`dotnet ef migrations add AddGoalAcquisitionSources`).
  Verify: the migration's model snapshot diff touches only the `config` owned-JSON mapping;
  no relational column add/drop.
- [x] 2.3 Add the data rewrite to the migration `Up` as `migrationBuilder.Sql(...)` (Postgres
  jsonb): per `goal_type` and the presence of `config->'ascensionFarming'` /
  `config->'farmingLocationIds'`, build `config->'acquisitionSources'` as an ordered
  `jsonb` array (`Campaign` from `ascensionFarming.shardBattleIds ∪ mythicShardBattleIds`,
  or an Unlock goal's `farmingLocationIds`; append `Onslaught` when
  `ascensionFarming.source ∈ {Onslaught, Both}`; `[{ "kind":"Campaign","ids":[] }]` when
  neither); then drop `config->'ascensionFarming'` and clear an Unlock goal's
  `farmingLocationIds`; leave Rank/Ability `farmingLocationIds` untouched. `Down` throws
  `NotSupportedException`. Verify: covered by 6.2.

## 3. API request/response surface

- [x] 3.1 `CreateGoalEndpoint.cs`: add `List<AcquisitionSourceRequest>? AcquisitionSources`
  to `CreateGoalConfigRequest` (`AcquisitionSourceRequest(string Kind, List<string> Ids)`);
  remove `AscensionFarming` and the `AscensionFarmingRequest` record. Verify: `dotnet build`
  of `TacticusPlanner.Api` progresses past these types.
- [x] 3.2 `UpdateGoalEndpoint.cs`: add `List<AcquisitionSourceRequest>? AcquisitionSources`
  to `UpdateGoalRequest`; apply it to `goal.Config.AcquisitionSources` (a `null` clears to
  unrestricted campaign, mirroring `FarmingLocationIds`). Verify: an API test updates a goal's
  sources and reads them back.
- [x] 3.3 `GoalMapper.cs`: map `AcquisitionSourceRequest` → domain on create/combined-create;
  map domain → `GoalConfigResponse`; add `AcquisitionSourceResponse`; remove
  `AscensionFarmingResponse` and its wiring. Verify: `dotnet build` clean; response DTO
  serializes `acquisitionSources`.
- [x] 3.4 Confirm `CreateCombinedGoalsEndpoint.cs` needs no separate DTO change (it nests
  `CreateGoalConfigRequest`). Verify: a combined-create API test posts per-goal
  `acquisitionSources` and each goal round-trips.

## 4. Validation

- [x] 4.1 `CreateGoalValidator.IsValidConfig`: reject an entry whose `Kind` ∉
  `AcquisitionSourceKinds`; require an `Onslaught` entry's `Ids` to be empty; require each
  `Shop` id to match `<shopId>:<rewardType>` syntactically. Remove the `AscensionFarming`
  branch. Verify: unit tests for unknown kind, non-empty Onslaught ids, malformed shop id.
- [x] 4.2 `GoalTargetValidationService`: for `Campaign` entries, every id ∈ the character's
  regular ∪ mythic shard-battle-id sets (reuse `RegularShardBattleIds` /
  `MythicShardBattleIds`); for `Shop` entries, `shopId` is a known shop and `rewardType` is
  `shards_*` / `mythicShards_*`; `Onslaught` only for Character Ascension; `Onslaught` and
  `Shop` rejected for MoW. Replace the old `AscensionFarming` battle-id checks. Verify: unit
  tests for each rejection path and the accepted Character-Unlock-with-Shop case.

## 5. V1 import

- [x] 5.1 ~~Build `AcquisitionSources` from V1 farming data~~ — revised during apply:
  `V1GoalImportService`'s Unlock/Ascension translation (`case 2`/`case 3`) never populated
  `AscensionFarming` or shard `FarmingLocationIds` in the first place (V1's "Onslaught"
  import there is Onslaught *progress*, unrelated to a goal's farming source). There is no
  V1 farming-source data to map. Confirmed the service compiles unchanged and imported
  Unlock/Ascension goals get `AcquisitionSources == null` (unrestricted campaign), which is
  correct. Verify: full-solution build is clean (`GoalsOnlyImportReturnsSpecs...` and the
  other `V1ImportEndpointTests` pass unchanged).

## 6. Tests

- [x] 6.1 API/domain unit + endpoint tests for every scenario in
  `specs/goal-target-model/spec.md` (allow-list, per-kind id rules, entity/goal-type gating,
  removal of the old model from OpenAPI, `FarmingLocationIds` retention, update editing).
  Rewrote the `GoalsEndpointTests.cs` cases that used the removed `AscensionFarming`/slot
  model (server no longer enforces regular-vs-mythic per goal type — Campaign ids validate
  against the union, per design DA5) and added cases for the allow-list, run-based-kind id
  rejection, malformed/unknown shop ids, entity/goal-type gating (Character-only Shop,
  Character-Ascension-only Onslaught), and update-endpoint editing. Verify:
  `dotnet test tests/TacticusPlanner.Api.Tests` green (79 passed).
- [x] 6.2 `TacticusPlanner.Persistence.IntegrationTests`: seed goals matching each migration
  scenario (Both, Campaign-only, Unlock `FarmingLocationIds`, neither, Rank/Ability
  `FarmingLocationIds`), run the migration, assert the resulting `acquisitionSources` /
  `farmingLocationIds`. Verify: integration test project green (2 passed, incl. the prior
  `RedesignGoalsProjectsManagement` migration test — confirms this migration doesn't
  regress it).
- [x] 6.3 OpenAPI contract check: no `AscensionFarming`; `acquisitionSources` present. No
  OpenAPI snapshot-test infrastructure existed in this repo to "update" — added a direct
  check instead (`GET /openapi/v1.json`, assert the document text excludes
  `AscensionFarming` and contains `acquisitionSources`). Verify: test passes.

## 7. Gates

- [x] 7.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` clean
  (exit 0; format also reflowed the new migration file to file-scoped namespace style).
- [x] 7.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore` clean — 0 warnings,
  0 errors.
- [x] 7.3 `dotnet test TacticusPlanner.slnx -c Release --no-build` green — 288/288 passed
  (89 GameCatalog, 2 Persistence.IntegrationTests, 197 Api.Tests).
- [x] 7.4 There is no committed OpenAPI document file in this repo — it's served at runtime
  only (`/openapi/v1.json`), verified content by 6.3's contract test; nothing to regenerate
  or commit here. The `tacticus-planner-apps` hand-written goal-type update is tracked as
  that repo's own task 1.2 in `add-shop-shard-acquisition-sources` — not done as part of
  this change.
