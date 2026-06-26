# Game Catalog Restructure — Handoff Summary

Repo: `D:/repos/tacticus/v2/tacticus-planner-api` (.NET 10). The Game Catalog = manifest-driven sync:
clients read `/api/v1/game-catalog/manifest`, compare per-dataset hashes, re-download changed datasets.
As of round 10 the **served surface is denormalized**: raw embedded `Data/**.json` files remain the
source, but the manifest exposes a small set of consolidated, self-contained datasets (reference tables
inlined; client never joins) computed at runtime in the `TacticusPlanner.GameCatalog` project. Per-dataset
hashes are computed over the serialized **denormalized projection**. Data is committed (no runtime
generator for raw files); one-off Python transforms in `tools/transform_catalog*.py`.

## Status: rounds 1–12 complete, build clean, 14/14 tests pass
Served manifest now has **9 denormalized datasets**, schemaVersion **11**, gameVersion **1.40**.
Raw source remains **79 files** (internal `catalog-manifest.json`).

### Round 12 — Game Catalog rename + served-surface reshape
- Renamed the project/namespace/domain `Catalog` → `GameCatalog` and routes `/api/v1/catalog/*` →
  `/api/v1/game-catalog/*` (project `TacticusPlanner.GameCatalog`, `Features/GameCatalog`, tests renamed).
- **Equipment** served as a plain array with the matched per-rarity upgrade-cost ladder **inlined**
  (`upgradeLevels`); dropped the `{items, upgradeCostsByRarity}` wrapper.
- **mows** served as a plain array; the shared cost ladder is its own served dataset `mow-upgrade-costs`.
- **campaign-battles** split into `campaign-battles` (flat, keyed by battle id, each carrying
  `campaignGroupId`) + `campaign-definitions` (keyed by groupId: metadata + `battleIds`). Battle ids are
  globally unique (1316/1316).
- **lres** `id` is now the unit snowprint string (e.g. `emperLucius`); removed the separate
  `unitSnowprintId` field and the old numeric id. schemaVersion 10 → 11.
- Validator: added campaign-definition `battleIds` → campaign-battles id cross-ref; non-empty checks for
  the two new datasets. Denormalizer-only change (no raw-file edits).

### Round 1 — faction split
- `units.json`+`mows.json` → `Data/units/units-{factionId}.json` = `{ alliance, factionId, name, characters[], mows[] }` (22 factions). Characters dropped client-only props (`title/fullName/shortName/extraShortName/requiredInCampaign/campaignsRequiredIn/releaseDate`) and gained inline `rankUpUpgrades`.
- NPCs added from v1 `new-npc-data.json` → `Data/npcs/npcs-{factionId}.json` (+ `npcs-objects`).
- `campaign-battles.json` → 14 groups (by core characters) `Data/campaign-battles/`.
- Shared MoW cost table → `mow-upgrade-costs.json`.
- Served via parameterized routes `/catalog/units/{factionId}`, `/catalog/npcs/{factionId}`, `/catalog/campaign-battles/{group}`. `factionId` = v1 `snowprintId` (lowercased for slug).

### Round 2 — more splits
- `campaigns.json`+`campaign-events.json` merged into the 14 `campaign-battles-{group}` chunk objects.
- `equipment.json` → by type (5): `/catalog/equipment/{type}`.
- `upgrades.json` → by rarity (6): `/catalog/upgrades/{rarity}`.
- `lres.json` → per-event, kept only latest 3 (Lucius/Farsight/Uthar): `/catalog/lres/{event}` (key = unit snowprintId).

### Round 3 — slim groups + drop-chances (latest)
- Each `campaign-battles-{group}.json` = `{ groupId, faction, releaseType, coreCharacters[], difficulties[], battles[] }`. Removed `groupType` + embedded `campaigns[]`.
- Battles dropped `campaign` (kept `campaignType`). Empty potential → `potential: []`.
- Reward chances extracted → synced **`drop-chances.json`** (20 distinct `{ id:"num_den", numerator, denominator, effectiveRate }`); potential rewards reference `chanceId`. Route `/catalog/drop-chances`.

### Round 4 — campaign-battles slimming + chance enrichment + event renames (latest)
- Battles dropped `requiredCharacterSnowprintIds` (same-set duplicate of group `coreCharacters` in 13/14 groups; indomitus per-node distinctions intentionally dropped).
- Battle `campaignType` → **`difficulty`**, normalized to the group `difficulties` enum (`standard`/`elite`/`mirror`, `eventStandard`/`eventChallenge`/`eventExtremis`). Authoritative battle→difficulty recovered from the pre-transform originals in git HEAD (`campaigns.json`+`campaign-events.json`+`campaign-battles.json`), since round-3 dropped the per-battle `campaign` link.
- The six campaign-EVENT groups renamed (file + `groupId`) to `<playableFaction>-vs-<enemyFaction>` (playable = faction of `coreCharacters`, enemy = old event groupId): `admech→death-guard-vs-admech`, `death-guard→adepta-sororitas-vs-death-guard`, `tyranids→ultramarines-vs-tyranids`, `tau-empire→genestealers-vs-tau-empire`, `adepta-sororitas→world-eaters-vs-adepta-sororitas`, `dark-angels→necrons-vs-dark-angels`. Standard campaigns unchanged.
- **`drop-chances.json`** rebuilt (20→**33** rows): `chanceId` is now `{rewardKind}_{difficulty}_{num}_{den}` (e.g. `upgradeCommon_eventStandard_12_13`, `shard_eventChallenge_2_6`); rows gained `rewardKind` (upgrade rarity / `shard` / `mythicShard`) + `difficulty` columns.
- schemaVersion bumped **4 → 5**.

### Round 5 — playable faction + stable chanceIds (latest)
- Each group's `faction` now corresponds to the **playable** faction (= faction of its `coreCharacters`, looked up from the units datasets). Round-4 had left two event groups carrying the enemy faction: `necrons-vs-dark-angels` (`DarkAngels`→**`Necrons`**) and `world-eaters-vs-adepta-sororitas` (`Sisterhood`→**`WorldEaters`**); all others were already correct.
- `chanceId` dropped its `{num}_{den}` suffix (rates can be rebalanced, so they shouldn't be baked into a stable id): `chanceId` is now **`{rewardKind}_{difficulty}`** (e.g. `upgradeCommon_eventStandard`, `shard_eventExtremis`). drop-chances.json keeps its `numerator`/`denominator`/`effectiveRate` columns (the live rate data) — only the id changed.
- To keep `{rewardKind}_{difficulty}` unique, the conflated **`eventChallenge`** difficulty was split into **`eventStandardChallenge`** / **`eventExtremisChallenge`** (every event has both a "Standard Challenge" and an "Extremis Challenge" campaign track with different drop rates; round-4 had collapsed both to `eventChallenge`). The parent tier is recovered from the original campaign names in git HEAD. Event group `difficulties` enum is now `[eventStandard, eventStandardChallenge, eventExtremis, eventExtremisChallenge]`.
- drop-chances.json stays **33** rows (the split exactly accounts for the former num/den collision — no data loss). No C# model/test changes needed (difficulty/chanceId/faction are validated generically).
- schemaVersion bumped **5 → 6**.

### Round 6 — challenge battle id normalization (latest)
- Stripped the leading zero from the trailing node segment of the 12 event-Challenge battle ids: `DGSC03B`→**`DGSC3B`** (node 3), while `…13B` / `…25B` are unchanged. Scope = `eventStandardChallenge`/`eventExtremisChallenge` battles only; standard/elite/mirror/event nodes keep their zero-padded ids.
- No schemaVersion bump: battle ids aren't referenced by any other catalog dataset and aren't stored in the manifest (it holds keys/files only; hashes are computed at runtime), so this is a content-only change picked up by the normal hash sync.

### Round 7 — canonical enums reference + rank normalization (latest)
- New plain reference file **`Data/enums.json`** cataloguing the canonical (space-free, id-like) form plus a `displayName` for all five enumerated dimensions: `ranks` (20), `rarities` (6), `factions` (22), `traits` (59), `alliances` (4). Each entry is `{ id, displayName }`. **Reference only** — intentionally NOT in the manifest (the round-5 regen only globs the split subdirs + explicit single-file appends, so a root-level `enums.json` is never auto-added), not served, not validated.
- **Rank values normalized** to one space-free form `<Tier><arabic>` (e.g. `Stone1`, `Iron2`, `Diamond3`). Round 6 and earlier left ranks inconsistent: `units/*.json` `rankUpUpgrades[].rank` used spaced Roman (`"Stone I"`) and `campaign-battles/*.json` `detailedEnemyTypes[].rank` used spaced Arabic (`"Iron 2"`); both are now `Stone1`/`Iron2`. `displayName` in enums.json keeps the in-game Roman label (`"Stone I"`). Rarities/factions/traits/alliances were already space-free, so only enums.json catalogs them (no data rewrite).
- No schemaVersion bump: `rank` is a free-form display string referenced by no other dataset and not stored in the manifest, so it's a content-only change picked up by the normal hash sync. No C#/model/test changes (rank value is never validated or referenced).

### Round 8 — LRE track requirements/restrictions (latest)
- Moved the per-track LRE "common data" out of the v1 hand-written TS classes (`tacticus-planner/src/fsd/3-features/lre/model/*.le.ts`) into the 3 v2 `lres/lres-*.json` files (`emperlucius`/`taufarsight`/`votanuthar` = v1 `10-lucius`/`11-farsight`/`12-uthar`). Each `alpha`/`beta`/`gamma` track gained two fields:
  - **`unitsRestrictions`** — the 5 objectives, each `{ name, points, iconId, index, filter }`. (The C# model already declared an empty `UnitsRestrictions`; this populates it and extends `CatalogLreRestriction` with `index` + `filter`.) The v1 `units` array is computed at runtime from the filter, so it is NOT stored.
  - **`allowedUnitsFilter`** — the track-level allowed-units restriction (alliance/faction pre-filter, AND-combined, all exclusions). Captures the 2 compound tracks (Lucius β = no Chaos + no Tyranids, Farsight γ = no Chaos + no Orks) that the track display name silently dropped.
- Filters use one structured shape **`{ kind, target, exclude }`** (reused for objective and track-level). v1's `objectiveType` maps to `kind`/`exclude`: `Not*` → `exclude:true`; `HasRangedAttack`/`HasNoRangedAttack` → `kind:"AttackType", target:"Ranged"` (melee = exclude ranged); `Min/MaxHits` keep the comparator in `kind` with `target` = number-as-string.
- C# (`GameCatalogModels.cs`): added `CatalogLreFilter(Kind, Target, Exclude)`; extended `CatalogLreRestriction` (+`Index`, +`Filter`) and `CatalogLreTrack` (+`AllowedUnitsFilter`). No endpoint/route changes — `/catalog/lres/{event}` serializes by reflection. `CatalogValidator` unchanged (no new cross-ref checks on objective targets).
- schemaVersion bumped **6 → 7** (served lres shape grew). Transform is idempotent.

### Round 9 — equipment cost-ladder extraction (latest)
- The 5 `equipment/equipment-*.json` datasets (212 items) repeated three cost fields (`goldCost`,
  `salvageCost`, `mythicSalvageCost`) on **every** level. The cost ladder is **fully determined by
  rarity** — exactly one distinct ladder per rarity, and the level count is fixed per rarity
  (Common 3, Uncommon 5, Rare 7, Epic 9, Mythic 10, Legendary 11). Only `stats` vary per item.
- Extracted the ladders into a new shared single-file dataset **`equipment-upgrade-costs.json`**
  (sibling of `mow-upgrade-costs.json`): a list of 6 `{ rarity, levels[] }` where each level is
  `{ goldCost, salvageCost, mythicSalvageCost }`. Route **`/catalog/equipment-upgrade-costs`**.
- Stripped the three cost fields from every item level → each `levels[i]` is now `{ stats: {...} }`
  (1,636 level entries slimmed). A consumer rebuilds an item's cost by looking up its `rarity` in the
  shared table and aligning by level index (item level i ↔ ladder index i).
- C# (`GameCatalogModels.cs`): added `CatalogEquipmentUpgradeCost(Rarity, Levels)` +
  `CatalogEquipmentUpgradeLevel(GoldCost, SalvageCost, MythicSalvageCost)`, the
  `EquipmentUpgradeCosts` dataset const (in `Required`), and the `CatalogSnapshot` field. Loader +
  endpoint (`GetCatalogEquipmentUpgradeCostsEndpoint`) + OpenAPI route mirror `mow-upgrade-costs`.
  `CatalogEquipment.Levels` stays opaque (`IReadOnlyList<JsonElement>`), so the stripped item levels
  need no model change. `CatalogValidator` unchanged.
- schemaVersion bumped **7 → 8** (equipment served shape shrank + a new served dataset). The transform
  hardcodes the canonical ladders and asserts any still-present cost fields match before stripping, so
  it is self-validating and idempotent.

### Round 10 — server-side denormalization + upgrades decomposition + gameVersion + anonymous (latest)
Big shift: **all denormalization moved server-side**, and the manifest now serves consolidated datasets
instead of the ~74 raw chunks.
- **Upgrades raw decomposition** (only raw-layout change): each rarity's craftable items split into
  `upgrades/upgrades-{rarity}-crafted.json` (skipping Common, which has none) — 6 base + 5 crafted = 11
  raw upgrade files. `tools/transform_catalog_round10.py` (idempotent, self-validating) also rewrites the
  internal source `catalog-manifest.json` with `gameVersion: "1.40"` and schemaVersion **8 → 9**.
- **Served surface = 7 denormalized datasets** (`characters`, `npcs`, `mows`, `upgrades`, `equipment`,
  `campaign-battles`, `lres`), each at `/catalog/{key}` returning a `CatalogDatasetEnvelope<TPayload>`
  (version/schemaVersion/gameVersion/sourceHash/datasetKey/datasetHash/data). Reference tables inlined:
  drop-chance rates onto campaign rewards + shard/upgrade farm locations; mow + equipment cost ladders at
  dataset level. Enrichments: characters get faction/alliance + shard farm locations + eligible equipment
  per slot; upgrades get farm locations + recursively expanded recipe split into base vs crafted totals;
  lres tracks get `availableUnitIds` (round-8 `allowedUnitsFilter` applied to the roster). The raw chunk
  endpoints (`/units/{faction}`, `/upgrades/{rarity}`, `mow-upgrade-costs`, `drop-chances`, …) are gone.
- New `CatalogDenormalizer` (Catalog project) builds the projections from the raw collections;
  `EmbeddedCatalogProvider` computes served `DatasetHashes` over each projection's canonical JSON and the
  `SourceHash` over (version, schemaVersion, gameVersion, served hashes). `CatalogSnapshot` keeps the raw
  collections (for `CatalogValidator`, which still checks raw cross-refs) plus the served payloads.
- **gameVersion** added to `CatalogManifest`/`CatalogSnapshot`/`CatalogManifestResponse` + every dataset
  envelope. **Anonymous**: catalog endpoints call `AllowAnonymous()` and the global `Program.cs`
  `Endpoints.Configurator` skips `catalog/` routes (every other endpoint keeps `AccessAsUser`).
- **Snapshot test** (`tests/TacticusPlanner.Api.Tests/__snapshots__/`, no new dependency): manifest
  registry (keys+urls+versions, hashes excluded) + the full `lres` body; regenerate with
  `UPDATE_SNAPSHOTS=1`. Plus an anonymous-access test (no-token client → 200).

### Round 11 — LRE static battle/enemy data (latest)
- Imported v1's static LRE battle data (`tacticus-planner/src/fsd/1-pages/plan-lre/new-le-battle-data.json`,
  `legendaryEvents` ids 12/13/14) into the 3 raw `lres/lres-*.json` files. Each track (alpha/beta/gamma)
  gained a **`battles`** array (18 per track, 162 total): `{ mapId, number, power, tier, disallowedFactions,
  waves[] }`, each wave `{ round, power, enemies[] }`, each enemy `{ id, stars, count }` (parsed from v1
  `"npcId:stars"` wave entries, duplicates aggregated). Per-battle `objectives` were dropped (they duplicate
  the track `unitsRestrictions`). The per-track **`defeatAll`** points array (already in the raw json) is now
  surfaced too.
- C# (`GameCatalogModels.cs`): added `CatalogLreBattle`/`CatalogLreWave`/`CatalogLreEnemy`; extended
  `CatalogLreTrack` + denormalized `CatalogLreTrackView` with `DefeatAll` + `Battles`. `BuildTrackView`
  passes them through (no enrichment — raw enemy ids). `CatalogValidator` now cross-checks every LRE enemy
  `id` against the npcs ids (all 84 resolve).
- `tools/transform_catalog_round11.py` (reads the v1 file once; idempotent + self-validating: 18 battles per
  track, enemies resolve). schemaVersion bumped **9 → 10**. The `catalog-lres.json` snapshot baseline was
  regenerated (now carries `defeatAll` + `battles`).

## Key files
- Models/registries: `src/TacticusPlanner.GameCatalog/GameCatalogModels.cs` (`GameCatalogDatasets` holds the dataset registries + `Required`).
- Loader: `src/TacticusPlanner.GameCatalog/EmbeddedGameCatalogProvider.cs` (leaf-filename resource resolution handles subfolders).
- Validation: `src/TacticusPlanner.GameCatalog/GameCatalogValidator.cs`.
- Denormalization: `src/TacticusPlanner.GameCatalog/GameCatalogDenormalizer.cs` (builds the 9 served projections from the raw collections).
- Endpoints: `src/TacticusPlanner.Api/Features/GameCatalog/` (`ServedDatasetEndpoint<TPayload>` base → one endpoint per served entity; manifest in `GetGameCatalogManifestEndpoint`).
- OpenAPI route metadata + anonymous configurator: `src/TacticusPlanner.Api/Program.cs` (`catalogOpenApiRoutes`, `Endpoints.Configurator`).
- Tests: `tests/TacticusPlanner.GameCatalog.Tests/GameCatalogValidationTests.cs`, `tests/TacticusPlanner.Api.Tests/GameCatalogApiSmokeTests.cs`, `tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests.cs` (+ `__snapshots__/`).
- Transforms: `tools/transform_catalog.py` (r1), `transform_catalog_round2.py`, `transform_catalog_round3.py`, `transform_catalog_round4.py` (r4 reads pre-transform originals from git HEAD), `transform_catalog_round5.py` (r5 reads original campaign names from git HEAD to split `eventChallenge`), `transform_catalog_round6.py` (r6 normalizes challenge battle ids), `transform_catalog_round7.py` (r7 normalizes `rank` values to space-free ids and emits the `enums.json` reference; idempotent), `transform_catalog_round8.py` (r8 injects LRE per-track `unitsRestrictions`+`allowedUnitsFilter` from the v1 `*.le.ts` data, bumps schemaVersion to 7; idempotent), `transform_catalog_round9.py` (r9 extracts the equipment cost ladder into `equipment-upgrade-costs.json` and strips cost fields from item levels, bumps schemaVersion to 8; self-validating + idempotent), `transform_catalog_round10.py` (r10 decomposes raw upgrades into per-rarity base/crafted files, adds `gameVersion`, bumps schemaVersion to 9; self-validating + idempotent), `transform_catalog_round11.py` (r11 injects v1 static LRE battle/enemy data into the raw lres files, bumps schemaVersion to 10; reads the v1 repo once, self-validating + idempotent).

## How to verify
`dotnet build TacticusPlanner.slnx` (build-time OpenAPI gen boots the app — fails fast on missing/invalid data) then `dotnet test TacticusPlanner.slnx`.

## Remaining follow-ups
1. **Not committed** — all round 1–10 changes are uncommitted working-tree edits (no branch/commit made yet).
2. **Frontend** (`D:/repos/tacticus/v2/tacticus-planner-apps`) — consume the 9 denormalized datasets (no client joins). Planned: extract a `packages/game-catalog` package, make sync manifest-driven (the stale hardcoded dataset-key array is wrong), store datasets + manifest in IndexedDB, react-router landing/home/redirect routes, a full-screen init loader (first-use vs re-sync, showing `gameVersion`).
3. **Docs repo** (`tacticus-planner-docs`) — refresh ERD / data-architecture for the denormalized served catalog.
4. Consider a combined/idempotent transform script (rounds are sequential and assume prior state).
5. `schemaVersion` is at 10, `gameVersion` is "1.40"; bump schemaVersion on any further structural change.
