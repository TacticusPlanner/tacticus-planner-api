---
name: game-catalog-data
description: How the server-side Game Catalog (TacticusPlanner.GameCatalog) is structured — embedded raw datasets, runtime denormalization into served datasets, hashing/manifest, the non-obvious data conventions (drop-chances, difficulty enums, LRE filters and the LRE split, equipment cost ladders), AND how to refresh it to a new in-game version from the V1 datamine. Use when adding/changing catalog data, the served projections, or bumping the game version.
---

# Game Catalog data

The Game Catalog is a **manifest-driven, server-side denormalized** dataset. Clients read
`/api/v1/game-catalog/manifest`, compare per-dataset hashes, and re-download only changed datasets. Raw
data lives as embedded JSON under `src/TacticusPlanner.GameCatalog/Data/**`; the served surface is a small
set of consolidated, self-contained datasets computed at runtime (reference tables inlined — the client
never joins). The served data carries only **structural/identity** fields: presentation is the client's job
(no `icon`/`roundIcon`/`iconId`/`wikiLink` on served records — the client derives icons and links from ids).
Raw `Data/**` records may still carry extra fields the models don't bind (`icon`, `wikiLink`, `eventStage`,
`iconId` on LRE restrictions); these are silently ignored on deserialize.

## Pipeline (where things live — read the code for current values, not this file)

- **Release metadata** — `Models/GameCatalogRelease.cs`: `Version` (human tag), `SchemaVersion` (served-payload
  contract version — bump **only on a breaking shape change** to a served dataset; a content-only change
  rides the per-dataset hash), `GameVersion` (the in-game version the embedded data was extracted from).
- **Dataset registry** — `Models/GameCatalogDatasets.cs`: the raw source keys (per-faction unit/npc lists,
  equipment types, upgrade rarities, campaign-battle groups, LRE events, plus single-file keys) and the
  `Served` list. Enumerate `GameCatalogDatasets.Served` for the authoritative set of served datasets.
- **Loading** — `GameCatalogLoader.Load()` (public; called eagerly at app startup in `Program.cs` to fail
  fast). Source files are discovered **by convention**: dataset key `foo-bar` → embedded `foo-bar.json`
  (matched by leaf filename, so subfolders don't matter). The csproj embeds `Data/**/*.json` by glob, so a
  new `{key}.json` only needs its key registered in `GameCatalogDatasets`. There is **no manifest file**.
- **Denormalization** — `Denormalization/*Denormalizer.cs` (`partial class GameCatalogDenormalizer`, one
  file per entity) builds the served views from the raw collections.
- **Validation** — `Validation/*.cs` (`partial class GameCatalogValidator`) runs at load; throws on any
  error (missing/empty dataset, duplicate id, missing required field, unresolved cross-reference). It runs
  over the **raw** snapshot collections, so reshaping the served *views* never breaks the cross-ref checks.
  `ManifestValidation` also requires every served dataset non-empty and that each `campaign-definitions` /
  `lres` `battleIds` resolves to a served battle. `ReferenceValidation` checks: character/mow rank-up &
  recipe upgrade ids, equipment `allowedUnits`, campaign `coreCharacters` & reward shard/upgrade ids,
  **and every LRE `battles.waves.enemies` id against the npc set**.
- **Hashing** — `Utils/GameCatalogHashing.cs`: per-dataset hash = canonical JSON (key-order-independent,
  array-order-sensitive) of the denormalized payload; `SourceHash` = hash of
  (version, schemaVersion, gameVersion, all dataset hashes).
- **Endpoints** — `src/TacticusPlanner.Api/Features/GameCatalog/`. `ServedDatasetEndpoint<TPayload>` base
  → one endpoint per served entity; manifest in `GetGameCatalogManifestEndpoint`. All `AllowAnonymous()`.

## Served datasets — non-obvious shapes

(`GameCatalogDatasets.Served` is the source of truth for the list. Each is served at
`/api/v1/game-catalog/{key}` in a `GameCatalogDatasetEnvelope<T>`.)

- **mows** is a plain array and lives **inside `units-{faction}.json`** (`mows[]`), not a separate file;
  the shared upgrade-cost ladder is its own dataset `mow-upgrade-costs`, keyed by the ability **level** it
  raises a MoW to (`level = rawIndex + 2`; level 1 is free).
- **ascension-costs** is the shared `(rarity, stars)` orb/shard ladder — one shared progression, served as
  its own dataset (ported from V1's `charsProgression`). **unlock-shard-costs** is the per-starting-rarity
  shard cost to unlock a character (ported from V1's `charsUnlockShards`) — also a single shared table.
- **onslaught-rewards** is keyed by sector+tier id; each entry carries a `regular` reward-range list plus
  one `mythic` range (`{min, max}`, with a computed `midpoint`).
- **upgrades** carry a **nested recipe** tree: each craftable ingredient embeds its own `recipe`
  (recursively; cycle-guarded); base materials have `recipe: null`. No separate "expanded totals" table.
- **equipment** is a plain array (raw files split by item type), each item carrying its per-rarity cost
  ladder inlined as `upgradeLevels` in the served view. The ladder is fully determined by rarity (exactly
  one per rarity; level counts fixed per rarity), sourced from `equipment-upgrade-costs.json`. Raw
  `levels[].stats` is the per-level stat block (stat-name→number map; keys vary by equipment type).
  `abilityId` is required.
- **campaign-battles** is flat, keyed by globally-unique battle id, each carrying its `campaignGroupId`;
  **campaign-definitions** is keyed by `groupId` (metadata + `battleIds[]` only). Battle ids are also
  referenced from API test fixtures and the player-data transformer, so renaming a battle id is not a
  local change.
- **LRE is split three ways** (mirroring campaign-battles / campaign-definitions):
  - **`lres`** — the lightweight per-event list, keyed by the unit snowprint string (not a numeric id).
    Each track (alpha/beta/gamma) resolves `availableUnitIds` at runtime (apply `allowedUnitsFilter` to
    the character roster — so adding a character changes every track that doesn't exclude its
    alliance/faction) and references its battles by `battleIds`. Event timing is `eventStageStartDatesUtc`
    (ISO-8601-UTC array, one element per stage; the client derives the current stage from it).
  - **`lre-battles`** — the bulky per-battle records (`waves` trees), keyed `"{lreId}-{track}-{number}"`,
    tagged with `lreId` + `track`.
  - **`lre-common`** — a reward ladder (`pointsMilestones`, `chestsMilestones`, `progression`,
    `shardsPerChest`) **assumed shared across events**, so served once — sourced from the
    alphabetically-first `lres` dataset (`BuildLreCommon` takes `.First()` after an ordinal sort). It is
    **not actually guaranteed identical** — e.g. the chest ladder length has changed between patches, so
    older-format event files can lag newer ones. If events genuinely diverge, revisit `BuildLreCommon`.
- **events** — `event-definitions` is served roughly as-is (structural rules); `events-calendar` is a
  date-indexed projection built from `event-definitions` + `event-occurrences` relative to load-time
  "now", so its hash (and `sourceHash`) legitimately changes every process start.

## Cross-cutting data conventions

- **drop-chances** (`drop-chances.json`): potential campaign rewards reference `chanceId` =
  `{rewardKind}_{difficulty}` (rates not baked into the id, so they can be rebalanced). Rows carry
  `numerator`/`denominator`/`effectiveRate`. Denormalization inlines these onto potential rewards and onto
  character shard / upgrade farm locations. **Guaranteed** locations/rewards (and unmatched chanceIds)
  carry `null` chance fields by design.
- **difficulty enum**: standard campaigns use `standard`/`elite`/`mirror`; event campaigns carry a
  battle-level `type` (`Standard`/`Extremis`) plus a `challenge` flag.
- **LRE filters** use one shape `{ kind, target, exclude }`, reused for per-objective (`unitsRestrictions`)
  and track-level (`allowedUnitsFilter`) filters. `kind` ∈ `Alliance`/`Faction`/`DamageType`/`Trait`/
  `MinHits`/`MaxHits`/`AttackType`; `exclude` flips the match. The runtime roster filter only executes
  `Alliance`/`Faction` kinds and ANDs all track filters.
- **rank** values are space-free `{Tier}{arabic}` (e.g. `Stone1`, `Iron2`, `Adamantine2`); the in-game
  Roman label lives only in `Data/enums.json` (a reference file — not served, not in the registry).
- Most nullable fields are genuinely conditional. Fields always present in the static data are required.
  Numeric stat fields on npcs are **non-nullable** — a missing value in the source must be written as `0`.

## Updating to a new in-game version

**There is no re-runnable pipeline.** The original raw data came from one-off Python transforms that were
removed; `Data/**` is now the committed source of truth, refreshed by hand against the V1 datamine.

**Source of truth for what changed:** the V1 frontend repo (`tacticusplanner`), `develop` branch,
`src/fsd/4-entities/*/data/*.json`. Find the datamine commits (messages like `"1.41 from the apk"`,
`"Sync game data for the new patch"`) to scope the diff, but reconcile against the *endpoint state* of the
target version, not the commit diffs (those are noisy with reformatting).

**Method: reconcile per dataset, don't wholesale-replace.** For each catalog dataset, diff the V1 source
at the target version against the current catalog: compare the **id set** (adds / removes / renames) and
then **field-by-field** for common ids. Apply the real content changes; ignore representation churn.
V1's datamine has regressed fidelity in places (post-1.41 it stopped populating npc ability-damage-type
arrays and dropped zero-valued fields) — porting such a file verbatim is a *downgrade*; take only the
genuine game changes.

**V1 → catalog field mapping (the non-obvious parts):**

- **characters** (`units-{faction}.json` `characters[]`): from V1 `character/data/new-character-data.json`
  (`Name`→`name`, `Health`→`health`, `Melee Damage`→`meleeDamage`, `Distance`→`rangeDistance`,
  `Equipment1..3`→`equipmentSlots`, `Active Ability`/`Passive Ability` arrays → `activeAbilityDamage`/
  `passiveAbilityDamage` — absent ⇒ `[]`) joined with `new-rank-up-data.json` (Roman rank keys
  `"Stone I"` → `Stone1`). `new-character-data2.json` is **not** used by the catalog.
- **mows** (`units-{faction}.json` `mows[]`): from V1 `mow/data/new-mow-data.json` `mows[]`
  (`snowprintId`→`id`; `primaryAbility`/`secondaryAbility` each `{name, recipes[][]}`); shared
  `upgradeCosts` → `mow-upgrade-costs.json`.
- **npcs** (`npcs-{faction}.json` `npcs[]`, faction-less ones in `npcs-objects.json`): from V1
  `npc/data/new-npc-data.json` (`Name`→`name`, `Stats[]`→`stats[]` with camelCase keys and
  `Armor`→`armour`; **missing numeric stat fields ⇒ `0`**). Ability-damage keys are the newer
  `Active Ability Damage`/`Passive Ability Damage`, falling back to the older `Active Ability`/
  `Passive Ability`.
- **equipment** (`equipment/equipment-{type}.json`): from V1 `equipment/data/new-equipment-data.json`
  (keyed by id in a dict; the catalog record adds `id` and keeps only `levels[].stats`, dropping V1's
  per-level cost fields — cost comes from `equipment-upgrade-costs.json`).
- **upgrades**: from V1 `upgrade/data/new-recipe-data.json`.
- **campaign-battles**: from V1 `campaign/data/new-battle-data.json`. The catalog shape is heavily
  denormalized (resolved enemy tables, rank labels) with no surviving transform — a full re-derivation is
  impractical; scope changes to id adds/removes and reward changes, and be wary that battle-id renames
  ripple into API tests + the player-data transformer.
- **LRE**: V1 migrated to a machine-generated format (`lre/data/{n}-{unit}.json` +
  `lre/data/new-le-battle-data.json` `legendaryEvents[]` + hand-kept `lre/data/lre-event-dates.ts`). The
  catalog's raw `lres-*.json` shape mirrors V1's *older* hand-authored format, so derive it using the
  semantics in V1's `lre/data/new-format-adapter.ts` — `buildStaticLegendaryEvent`, `buildTrackName`
  (reconstructs `"Alpha (No Xenos)"` from `disallowedFactions`), `questsToMissionText` (task→string),
  `toLegacyPointsMilestones` / `toLegacyChestMilestones`. Wave enemies are `"{npcId}:{stars}"` strings;
  aggregate identical `(id, stars)` to `{id, stars, count}`. `bonusObjectives` → `unitsRestrictions`
  (`{name, points, iconId, filter:{kind,target,exclude}, index}` — `iconId` is not served). Track
  `allowedUnitsFilter` derives from `disallowedFactions` (a full alliance roster ⇒
  `[{kind:Alliance, target:<alliance>, exclude:true}]`). Add/remove events by editing
  `GameCatalogDatasets.LreEvents` and adding/removing the `lres-{unit}.json` file. Every wave enemy id
  must already exist as an npc (add the npcs first).
- **client-side companion** (`tacticus-planner-apps`): new characters/equipment/traits need assets under
  `apps/web/public/game_catalog/` (portraits `characters/ui_image_RoundPortrait_{slug}_01.png` + full
  `ui_image_portrait_*`, equipment `equipment/ui_icon_item_{id}.png`, traits
  `traits/ui_icon_trait_{slug}_01.png`), an entry in
  `packages/game-catalog/src/game-entities/character-icon-overrides.ts` when the id→slug derivation
  diverges from the asset filename, and display names in `apps/web/public/locales/en/characters.json`.
  Source assets from `tacticusplanner/src/assets/images/snowprint_assets/`.

**Finish:** bump `GameVersion` (and usually `Version`) in `GameCatalogRelease.cs`; leave `SchemaVersion`
unless a served shape changed. Then run the checklist below and promote the manifest snapshot.

## Editing checklist

1. Edit the raw `Data/**` json (or add a new `{key}.json` + register its key in `GameCatalogDatasets`).
   Keep files LF, 2-space indent, trailing newline (`json.dumps(d, indent=2, ensure_ascii=False) + "\n"`
   round-trips; force `newline="\n"` when scripting on Windows).
2. If a **served shape** changes: update the relevant `Models/*` view record + the
   `Denormalization/*Denormalizer` builder + any `Validation/*` cross-ref, and bump
   `GameCatalogRelease.SchemaVersion`.
3. `dotnet build TacticusPlanner.slnx` (startup load + validation fails fast on bad data), then
   `dotnet test tests/TacticusPlanner.GameCatalog.Tests/...` and
   `dotnet test tests/TacticusPlanner.Api.Tests/...`.
4. The manifest snapshot is an **API-level** Verify test
   (`tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests`) that hits the live manifest endpoint;
   `sourceHash` and the `events-calendar` hash are scrubbed as `{time-dependent}`. On a mismatch, review
   the `*.received.txt` diff (expect: `gameVersion`/`version` plus exactly the dataset hashes you touched),
   then promote it to `*.verified.txt`. Run with `DiffEngine_Disabled=true` to suppress the diff-tool popup.
5. `dotnet format TacticusPlanner.slnx --verify-no-changes`.
