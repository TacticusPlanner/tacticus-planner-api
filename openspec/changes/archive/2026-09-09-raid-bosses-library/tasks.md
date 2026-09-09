## 1. Raw data authoring

- [x] 1.1 Port `guild_boss.json` from `tacticusplanner` into authored raw sources under `Data/raid-bosses/` — season rotation, unit sets, season configs, and modifier definitions, via `scripts/port-raid-boss-data.mjs` (drops `misc`/`boards`/`visualId`/`spawnPointsSet`/`seasonEndRewards`; keeps `nrOfMembers` and `relicAbilityLevel`). `dotnet build` loads + validates it without error.
- [x] 1.1a **File family, not one blob** (Decision 8): authored as `raid-boss-<n>-<Type>.json` (one per boss, 12 — e.g. `raid-boss-11-TauRiptide.json`, `<Type>` = the `Boss1` key minus its `GuildBoss<n>Boss1` prefix, verbatim), `raid-boss-season-<n>.json` (one per season config, 5), and `raid-boss-common.json` (rotation + primarchs + modifier defs), mirroring `lres-<event>` + `lre-common`. The former single `raid-boss-data.json` (~1.3 MB) is removed. `scripts/port-raid-boss-data.mjs` emits the family and wipes stale `raid-boss-*.json` on each run.
- [x] 1.2 `GameCatalogRelease.GameVersion` — **left unchanged at `1.42`**. It is a single catalog-wide value and the ported datamine is from the same 1.41→1.42 era as the rest of the embedded data; bumping it would falsely re-tag every other dataset. Recorded here rather than changed.
- [x] 1.3 No display/presentation fields in the raw source (the port script only emits structural fields). Verified by the shape test in 7.2.

## 2. Registry & models

- [x] 2.1 Registered the raw source keys `raid-boss-common` + `RaidBossGroups` (`raid-boss-1`..`12`) + `RaidBossSeasons` (`raid-boss-season-1`..`5`) in `Required`, and the served key `raid-bosses` in `Served`, in `Models/GameCatalogDatasets.cs`. `GameCatalogLoader.LoadRaidBossRawData` merges the family into one `GameCatalogRaidBossRawData` (throws on a duplicate unit-set key or season-config id); `GameCatalogRaidBossGroupRawData` / `GameCatalogRaidBossCommonRawData` are the per-file binding records.
- [x] 2.2 Added served-view records in `Models/RaidBosses.cs`: `GameCatalogRaidBossesView` (`SeasonConfigRotation`, `Bosses`, `Primes`, `Seasons`), `GameCatalogRaidBossView`, `GameCatalogRaidBossStatStepView` (core stats non-nullable; `RelicAbilityLevel`/crit/block optional), `GameCatalogRaidBossWeaponView` (optional `Range`), `GameCatalogRaidBossSeasonView`/`TierView`/`SetView`, `GameCatalogRaidBossEncounterView`, `GameCatalogRaidBossEncounterModifierView`. `subtargets` (plural) dropped — the datamine only carries singular `subtarget`. Project compiles.
- [x] 2.3 Added the internal raw models (`GameCatalogRaidBossRawData` + nested) in the same file, used only by denormalization/validation.

## 3. Denormalization

- [x] 3.1 `Denormalization/RaidBossDenormalizer.cs`: `ClassifyRaidBossUnit` uses `^GuildBoss(\d+)Boss` / `^GuildBoss(\d+)(?:MiniBoss|Minion)(\d+)`; `bosses` ordered by boss number, `primes` by (boss number, prime index); `isPrimarch` from the raw `Primarchs` list. Covered by `RaidBossDenormalizerTests`.
- [x] 3.2 Classified unit sets mapped to their served view — full `StatProgression` in source order, `Weapons` with `Range` only when present, ability/trait id arrays omitted when empty, plus `FactionId`/`Movement`. Covered by 7.1.
- [x] 3.3 `Seasons` built preserving tier/set order with `ChestId`/`GuildXp`; `SeasonConfigRotation` in rotation order. Covered by 7.3.
- [x] 3.4 Per encounter: `UnitId` split into `UnitSetId` + 1-based `ProgressionIndex` (absent → `1`); `FieldNpcIds` = ordered de-duplicated union of `Npc1Id`/`Npc2Id`/`Enemies` with `:N` stripped; `BossType`/`DisallowedFactionIds` carried when present; `EncounterType` mapped. Covered by 7.4.
- [x] 3.5 Encounter modifier ids resolved against the raw `Modifiers` map and inlined as `{ ModifierId, Type, Target, Subtarget?, Amount }` alongside `HpLost`. Covered by 7.5.

## 4. Validation

- [x] 4.1 `Validation/RaidBossValidation.cs`: every encounter's main `UnitSetId` and every field-npc id resolves to a raw unit-set key; unresolved → `MissingReference` at load. Covered by 7.6.
- [x] 4.2 Every encounter modifier id resolves to a raw modifier definition; unresolved → `MissingReference`. (Season configs reference unit sets only through encounters, which 4.1 already covers.) Covered by 7.6.
- [x] 4.3 Shape checks (scoped to classified boss/prime unit sets — field npcs / loot objects are referenced by id only): non-empty `Stats`, `FactionId` required, no blank ability/trait id, `EncounterType` ∈ {`Boss`, `Crystal`}. Covered by 7.6.
- [x] 4.4 `ManifestValidation` requires the served `Bosses` and `Primes` both non-empty. Covered by 7.6.

## 5. Hashing & manifest

- [x] 5.1 `raid-bosses` added to the `datasetHashes` dictionary in `GameCatalogLoader.Load()` (the per-dataset hash + `SourceHash` pipeline; `GameCatalogHashing` itself has no key registry). The manifest response includes a `raid-bosses` hash entry.

## 6. Endpoints

- [x] 6.1 `GetGameCatalogRaidBossesEndpoint` added via `ServedDatasetEndpoint<GameCatalogRaidBossesView>` (`AllowAnonymous`), `GET game-catalog/raid-bosses`. No endpoints for the raw source. The regenerated `artifacts/openapi/TacticusPlanner.Api.json` includes the path + schemas (artifact is gitignored).

## 7. Tests & verification

- [x] 7.1 `RaidBossDenormalizerTests` — boss/prime classification + order, `isPrimarch`, progression round-trip, ranged vs melee `Range`, empty ability/weapon collections omitted.
- [x] 7.2 `RaidBossDenormalizerTests` + `GameCatalogLoaderTests.RaidBossesDatasetSplits...` — served records carry only structural fields (record shape has no name/icon members).
- [x] 7.3 `RaidBossDenormalizerTests.SeasonConfigRotationIsPreservedInOrder` + `...EncounterSplitsTheProgressionSuffix...` (tier/set nesting).
- [x] 7.4 `RaidBossDenormalizerTests.EncounterSplitsTheProgressionSuffixAndUnionsFieldNpcIds`.
- [x] 7.5 `RaidBossDenormalizerTests.EncounterModifierInlinesTheResolvedDefinition`.
- [x] 7.6 `RaidBossValidationTests` — unresolved encounter unit, unresolved field npc, unresolved modifier id, empty progression, unrecognized encounter type, missing faction, empty bosses/primes — each with a clean-data success case.
- [x] 7.7 `dotnet build TacticusPlanner.slnx -c Release` — passes (catalog load + validation succeed at startup / OpenAPI generation).
- [x] 7.8 `dotnet test TacticusPlanner.slnx -c Release --no-build` — the Verify manifest snapshot's only diff was the added `raid-bosses` hash entry (every other dataset hash unchanged, confirming additivity); `.received.txt` promoted to `.verified.txt`. Full run: GameCatalog 106/106, Api 200/200, Persistence.Integration 2/2.
- [x] 7.8a Splitting the single raw file into the per-boss/per-season/common family (Decision 8) left the served `raid-bosses` hash and the manifest snapshot **unchanged** — the canonical-JSON hash sorts object keys and the bosses/primes arrays are `OrderBy`-sorted in `BuildRaidBosses`, so the assembled data is byte-equivalent. `GameCatalogLoaderTests.RaidBossesDataset...` gained assertions that the first (`GuildBoss1Boss…`) and last (`GuildBoss12Boss1DarkaLion`) boss files both merge. Full run still GameCatalog 106/106, Api 200/200.
- [x] 7.9 Repository gates: `dotnet format --verify-no-changes` clean, `dotnet build -c Release` clean, `dotnet test -c Release` green.

## 8. Cross-repo coordination

- [x] 8.1 Served `GameCatalogRaidBossesView` shape matches `specs/raid-bosses-dataset` in this change and the companion `tacticus-planner-apps` `raid-bosses-library` change's `raid-boss-catalog` spec (`{ seasonConfigRotation, bosses[], primes[], seasons{} }`, id-only). No spec drift.

## Deferred / out-of-session

- [ ] D.1 Full-stack manual verification that the apps `raid-bosses-library` page renders the served dataset — belongs to the companion apps change's apply, tracked by `tacticus-planner-apps#86`.
