---
name: game-catalog-data
description: How the server-side Game Catalog (TacticusPlanner.GameCatalog) is structured — embedded raw datasets, runtime denormalization into served datasets, hashing/manifest, and the non-obvious data conventions (drop-chances, difficulty enums, LRE filters and the LRE split, equipment cost ladders). Use when adding/changing catalog data or the served projections.
---

# Game Catalog data

The Game Catalog is a **manifest-driven, server-side denormalized** dataset. Clients read
`/api/v1/game-catalog/manifest`, compare per-dataset hashes, and re-download only changed datasets. Raw
data lives as embedded JSON under `src/TacticusPlanner.GameCatalog/Data/**`; the served surface is a small
set of consolidated, self-contained datasets computed at runtime (reference tables inlined — the client
never joins). The served data carries only **structural/identity** fields: presentation is the client's job
(there are no `icon`/`roundIcon`/`iconId`/`wikiLink` fields — the client derives icons and links from ids).

## Pipeline (where things live)
- **Release metadata** — `Models/GameCatalogRelease.cs` constants (`Version`, `SchemaVersion`,
  `GameVersion`). `SchemaVersion` is the served-payload contract version; bump it **only on a breaking shape
  change** to a served dataset (a content-only change rides the per-dataset hash via the normal sync).
  Currently `1` (there are no production users yet, so historical bumps were reset).
- **Dataset registry** — `Models/GameCatalogDatasets.cs`: the raw source keys (`UnitFactions`,
  `NpcFactions`, `EquipmentTypes`, `UpgradeRarities`, `CampaignBattleGroups`, `LreEvents`, + the
  single-file keys) and the 11 `Served` keys.
- **Loading** — `GameCatalogLoader.Load()` (public; called eagerly at app startup in `Program.cs` to
  fail fast). Source files are discovered **by convention**: dataset key `foo-bar` → embedded
  `foo-bar.json` (matched by leaf filename, so subfolders don't matter). There is **no manifest file**.
- **Denormalization** — `Denormalization/*.cs` (`partial class GameCatalogDenormalizer`, one file per
  entity) builds the served views from the raw collections.
- **Validation** — `Validation/*.cs` (`partial class GameCatalogValidator`) runs at load; throws on any
  error (missing/empty dataset, duplicate id, missing required field, unresolved cross-reference). It runs
  over the **raw** snapshot collections, so reshaping the served *views* never breaks the cross-ref checks.
  `ManifestValidation` also requires every served dataset non-empty and that each `campaign-definitions` /
  `lres` `battleIds` resolves to a served battle.
- **Hashing** — `Utils/GameCatalogHashing.cs`: per-dataset hash = canonical JSON (key-order-independent,
  array-order-sensitive) of the denormalized payload; `SourceHash` = hash of
  (version, schemaVersion, gameVersion, all dataset hashes).
- **Endpoints** — `src/TacticusPlanner.Api/Features/GameCatalog/`. `ServedDatasetEndpoint<TPayload>` base
  → one endpoint per served entity; manifest in `GetGameCatalogManifestEndpoint` (returns the domain
  `GameCatalogManifest`). All catalog endpoints are `AllowAnonymous()` (static public data).

## The 14 served datasets
`characters`, `npcs`, `mows`, `mow-upgrade-costs`, `ascension-costs`, `unlock-shard-costs`,
`onslaught-rewards`, `upgrades`, `equipment`, `campaign-battles`, `campaign-definitions`, `lres`,
`lre-battles`, `lre-common` (`GameCatalogDatasets.Served`). Each served at `/api/v1/game-catalog/{key}` in a
`GameCatalogDatasetEnvelope<T>` (version/schemaVersion/gameVersion/sourceHash/datasetKey/datasetHash/data).

Non-obvious shapes:
- **mows** is a plain array; the shared upgrade-cost ladder is its own dataset **`mow-upgrade-costs`**,
  keyed by the ability **level** it raises a MoW to (`level = rawIndex + 2`; level 1 is free).
- **ascension-costs** is the shared 20-step `(rarity, stars)` ladder — one shared progression, so served
  as its own dataset rather than inlined per character (ported from V1's `charsProgression`, reconciled
  against `OrbAscensionCalculator.UPGRADE_PATH`; the two V1 sources disagreed only at
  `Mythic:MythicWings`, resolved to 25 orbs). **unlock-shard-costs** is the per-starting-rarity shard cost
  to unlock a character (ported from V1's `charsUnlockShards`) — also a single shared table.
- **onslaught-rewards** is keyed by sector+tier id; each entry carries a `regular` reward-range list plus
  one `mythic` range (`{min, max}`, with a computed `midpoint`).
- **upgrades** carry a **nested recipe** tree: each craftable ingredient embeds its own `recipe`
  (recursively; cycle-guarded); base materials have `recipe: null`. No separate "expanded totals" table.
- **equipment** is a plain array, each item carrying its matched per-rarity cost ladder inlined as
  `upgradeLevels`. The ladder is fully determined by rarity (exactly one per rarity; fixed level counts:
  Common 3, Uncommon 5, Rare 7, Epic 9, Mythic 10, Legendary 11) — sourced from
  `equipment-upgrade-costs.json`. Each item's per-level stat block is `levels[].stats` (a stat-name→number
  map; the keys vary by equipment type). `abilityId` is required.
- **campaign-battles** is flat, keyed by globally-unique battle id, each carrying its `campaignGroupId`;
  **campaign-definitions** is keyed by `groupId` (metadata + `battleIds[]` only).
- **LRE is split three ways** (mirroring campaign-battles / campaign-definitions):
  - **`lres`** — the lightweight per-event list, keyed by the unit snowprint string (e.g. `emperLucius`),
    not a numeric id. Each track (alpha/beta/gamma) resolves `availableUnitIds` at runtime (apply
    `allowedUnitsFilter` to the character roster) and references its battles by **`battleIds`** (it no
    longer embeds them). Event timing is **`eventStageStartDatesUtc`** — an ISO-8601-UTC array of per-stage
    start dates (one element today); the client derives the current stage from it.
  - **`lre-battles`** — the bulky per-battle records (the `waves` trees), keyed `"{lreId}-{track}-{number}"`
    and tagged with `lreId` + `track`. A track's `battleIds` all resolve here (validated).
  - **`lre-common`** — the single shared reward ladder (`pointsMilestones`, `chestsMilestones`,
    `progression`, `shardsPerChest`); identical across every event, so served once (record id `lre-common`).

## Cross-cutting data conventions
- **drop-chances** (`drop-chances.json`): potential campaign rewards reference a `chanceId` =
  `{rewardKind}_{difficulty}` (rates are not baked into the id, so they can be rebalanced). Rows carry
  `numerator`/`denominator`/`effectiveRate`. Denormalization inlines these onto each potential reward and
  onto character shard / upgrade farm locations. **Guaranteed** locations/rewards (and unmatched chanceIds)
  carry `null` chance fields by design — those fields are genuinely nullable.
- **difficulty enum**: standard campaigns use `standard`/`elite`/`mirror`; event campaigns use
  `eventStandard`/`eventStandardChallenge`/`eventExtremis`/`eventExtremisChallenge` (the two "challenge"
  tiers are kept distinct because each has its own drop rates).
- **LRE filters** use one shape `{ kind, target, exclude }`, reused for per-objective
  (`unitsRestrictions`) and track-level (`allowedUnitsFilter`) filters. `kind` ∈ `Alliance`/`Faction`/…;
  `exclude` flips the match. The runtime roster filter ANDs all track filters.
- **rank** values are space-free `{Tier}{arabic}` (e.g. `Stone1`, `Iron2`); the in-game Roman label lives
  only in `Data/enums.json` (a reference file — not served, not in the registry).
- Most nullable fields are genuinely conditional (ranged stats, `forgeBadges`, nested `recipe`, the
  drop-chance join fields). Fields that are always present in the static data are typed as required.

## Editing checklist
1. Edit the raw `Data/**` json (or add a new `{key}.json` + register its key in `GameCatalogDatasets`).
2. If the served shape changes: update the relevant `Models/*` view record + the
   `Denormalization/*Denormalizer` builder + any `Validation/*` cross-ref, and bump
   `GameCatalogRelease.SchemaVersion` (breaking shape change only).
3. `dotnet build TacticusPlanner.slnx` (startup load + validation fails fast on bad data), then
   `dotnet test TacticusPlanner.slnx`. The manifest snapshot is guarded by **Verify** (`Verify.XunitV3`):
   on a mismatch the test writes `GameCatalogSnapshotTests.GameCatalogManifestMatchesSnapshot.received.txt`
   next to the test — review the hash diff, then promote it to the committed `*.verified.txt`. Run with
   `DiffEngine_Disabled=true` to suppress the diff-tool popup.

## History
Raw data was produced by one-off Python transforms (rounds 1–12) that are no longer part of the repo;
the embedded `Data/**` json is now the committed source of truth. If you need the provenance of a specific
field, the transform rationale is in the git history of the (removed) `tools/transform_catalog*.py`.
