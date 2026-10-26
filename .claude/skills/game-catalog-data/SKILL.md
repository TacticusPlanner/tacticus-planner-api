---
name: game-catalog-data
description: How the server-side Game Catalog (TacticusPlanner.GameCatalog) is structured — embedded raw datasets, runtime denormalization into served datasets, hashing/manifest, the non-obvious data conventions (drop-chances, difficulty enums, LRE filters and the LRE split, equipment cost ladders), AND how to refresh it to a new in-game version (bump the pinned tacticus-planner-data-mines REF; V1 datamine for datasets not yet migrated). Use when adding/changing catalog data, the served projections, or bumping the game version.
---

# Game Catalog data

The Game Catalog is a **manifest-driven, server-side denormalized** dataset. Clients read
`/api/v1/game-catalog/manifest`, compare per-dataset hashes, and re-download only changed datasets. Raw
data lives as embedded JSON under `src/TacticusPlanner.GameCatalog/Data/**`; the served surface is a small
set of consolidated, self-contained datasets computed at runtime (reference tables inlined — the client
never joins). The served data carries only **structural/identity** fields: presentation is the client's job
(no `icon`/`roundIcon`/`iconId`/`wikiLink` on served records — the client derives icons and links from ids).
Raw `Data/**` records may still carry extra fields the models don't bind (`icon`, `wikiLink`, `iconId` on LRE
restrictions); these are silently ignored on deserialize.

## Pipeline (where things live — read the code for current values, not this file)

- **Release metadata** — `Models/GameCatalogRelease.cs`: `Version` (human tag), `SchemaVersion` (served-payload
  contract version — bump **only on a breaking shape change** to a served dataset; a content-only change
  rides the per-dataset hash), `GameVersion` (the in-game version the embedded data was extracted from).
- **Dataset registry** — `Models/GameCatalogDatasets.cs`: the raw source keys (per-faction unit/npc lists,
  upgrade rarities, campaign-battle groups, LRE events, plus single-file keys) and the
  `Served` list. Enumerate `GameCatalogDatasets.Served` for the authoritative set of served datasets.
- **Loading** — `GameCatalogLoader.Load()` (public; called eagerly at app startup in `Program.cs` to fail
  fast). Source files are discovered **by convention**: dataset key `foo-bar` → embedded `foo-bar.json`
  (matched by leaf filename, so subfolders don't matter). The csproj embeds `Data/**/*.json` by glob, so a
  new `{key}.json` only needs its key registered in `GameCatalogDatasets`. There is **no manifest file**.
  `Data/curated/` holds hand-kept data with no game source (event definitions/occurrences, horde-mode tiers,
  guild-raid meta, `enums.json`); it is only a folder, so the leaf-name lookup is unchanged.
- **Datamine source** — `Data/datamine/` holds byte-identical copies of `tacticus-planner-data-mines` `raw/`
  files (see **Datamine source** below). They are embedded as `datamine/<raw path>` and opened with
  `DatamineFiles.Open("gameconfig/items.json")`, never by the leaf-name convention.
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

- **characters**, **mows** and **npcs** are built from the datamine `gameconfig/units/lineup.json` (characters
  and MoWs: a unit with the `MachineOfWar` trait is a MoW) and `gameconfig/units/npc.json`, with
  `gameconfig/units/abilities.json` for damage profiles and MoW recipes (`UnitDatamineReader.cs`). Grouped
  internally as `units-{faction}` / `npcs-{faction}` (lowercased `FactionId`; faction-less loot objects →
  `npcs-objects`, `Objects`/`Neutral`), ordered by id. Weapons: `weapons[0]` melee, `weapons[1]` ranged
  (`Range`); absent ⇒ `""`/0 melee, null ranged. Traits drop the game's `Hero`. Damage lists = distinct
  `damageProfile`/`damageProfile_N` constants of the abilities, sorted; ability ids keep game order. Character
  `rankUpUpgrades` = `upgrades` rows ranked Stone1…; MoW primary/secondary = its two actives with the
  abilities' `upgrades` recipes, `mythicAbility` = `mythicAbilities[0]`. NPC stats map `FixedArmor`→`armour`,
  `StarLevel`→`stars` (absent ⇒ 0). No faction display names (the record's `Name` holds the faction id).
- **mows** is a plain array; the shared upgrade-cost ladder is its own dataset `mow-upgrade-costs`, keyed by the ability **level** it
  raises a MoW to (`level = rawIndex + 2`; level 1 is free).
- **ascension-costs** is the shared `(rarity, stars)` orb/shard ladder — one shared progression, served as
  its own dataset, built from the datamine `gameconfig/units/heroProgressionSteps.json`. `stars` 0-14 map to
  `None` … `MythicWings` (progression `{rarity}:{label}`); `costs.shards`/`mythicShards`/
  `heroAscensionOrb{Rarity}` → `shards`/`mythicShards`/`orbs`+`orbRarity`. Row 0's game cost is the unlock, so
  `Common:None` is served free. Any other cost key fails startup, and validation requires exactly
  `ProgressionRules.ProgressionOrder`. **unlock-shard-costs** is the per-starting-rarity shard cost to unlock a
  character, built from the datamine `gameconfig/units/heroConversion.json` in rarity order (no Mythic: the game
  has none and no character starts there).
- **onslaught-rewards** is keyed by sector+tier id; each entry carries a `regular` reward-range list plus
  one `mythic` range (`{min, max}`, with a computed `midpoint`).
- **upgrades** carry a **nested recipe** tree: each craftable ingredient embeds its own `recipe`
  (recursively; cycle-guarded); base materials have `recipe: null`. No separate "expanded totals" table.
  Built from the datamine `gameconfig/upgrades.json`: `label`/`material` = the game's untranslated `name`
  (no `languages/*.json` in the catalog; localization is the apps' build step), `statType` `hp`/`dmg`/`fixedArmor` →
  `Health`/`Damage`/`Armour`, `crafting` → `craftable` + recipe. Grouped internally as
  `upgrades-{rarity}[-crafted]`, ordered by id. An empty name or unknown `statType` fails startup.
- **equipment** is built from the datamine `gameconfig/items.json` (keyed by item id; see **Datamine
  source**) and served as a plain array ordered by item type, then id. Each served level's `stats` is the
  game's stat block with five keys renamed to the names the dataset has always served (`critDmg`→`critDamage`,
  `critDmgBonus`→`critDamageBonus`, `blockDmg`→`blockDamage`, `blockDmgBonus`→`blockDamageBonus`,
  `fixedArmor`→`armor`); keys vary by equipment type. `upgradeLevels` is each level's own
  `goldCost`/`dustCost`/`mythicDustCost` (served as `goldCost`/`salvageCost`/`mythicSalvageCost`, absent ⇒ 0).
  In 1.43 every item of a rarity has the same ladder; `EquipmentDenormalizerTests` flags a bump that breaks
  that. Absent `abilityId` ⇒ `""`, `isRelic`/`isUniqueRelic` ⇒ false, `allowedUnits`/`allowedFactions` ⇒ `[]`.
- **character-ability-costs** is built from the datamine `gameconfig/units/abilityUpgradeCosts.json` (row N is the
  cost from ability level N to N + 1, served as `level: N + 1`). Each row is `gold` plus exactly one
  `abilityToken{Rarity}`; the key's suffix is the served `badges.rarity`. Any other row shape fails startup.
- **mow-upgrade-costs** is built from the datamine `gameconfig/units/abilityUpgradeCostsMoW.json` (row N, 0-based,
  serves `level: N + 2`): `dust` → `salvage` (absent ⇒ 0), `abilityToken{Rarity}` → `badges`,
  `itemAscensionResource_{Rarity}` → `forgeBadges` (absent ⇒ null), `machinesOfWarToken` → `components`. Any
  other key fails startup.
- **campaign-battles** is flat, keyed by globally-unique battle id, each carrying its `campaignGroupId`;
  **campaign-definitions** is keyed by `groupId` (metadata + `battleIds[]` only). Battle ids are also
  referenced from API test fixtures and the player-data transformer, so renaming a battle id is not a
  local change.
- **LRE is split three ways** (mirroring campaign-battles / campaign-definitions):
  - **`lres`** — the lightweight per-event list, keyed by the unit snowprint string (not a numeric id).
    Each track (alpha/beta/gamma) resolves `availableUnitIds` at runtime (apply `allowedUnitsFilter` to
    the character roster — so adding a character changes every track that doesn't exclude its
    alliance/faction) and references its battles by `battleIds`. Event timing is `eventStageStartDatesUtc`
    (`string[]` of `yyyy-MM-ddTHH:mm:ssZ`), served from the raw file's **`runStartDatesUtc`** (RFC 1123 or
    ISO 8601 UTC, normalised in order by `BuildEventStageStartDates`). **The index is the run number − 1**
    (element 0 starts run 1, element 2 run 3). Only announced runs are listed, as a **prefix with no gaps**
    (a later run cannot be listed before every earlier one); absent or `[]` serves `[]` ("no announced run").
    The client derives the current, ended and next run, and whether the event is finished (all 3 starts
    known and run 3's 7 days over), from the array (a run is a 7-day window); the server sends no run
    number, lifecycle or `finished` flag (the served catalog is hashed and must not depend on the clock).
    Raw files carry no `eventStage`, `nextEventDate`/`nextEventDateUtc` or `finished` either — all are
    derivable from `runStartDatesUtc`; don't re-add them when importing from V1. Enforced at load
    (`Validation/LreRunStartsValidation.cs`, `InvalidRunStartDates`): at most 3 entries, each parseable,
    strictly ascending, consecutive starts **at least 7 days apart** (closer would overlap). **To add the
    next run** when it is announced, append its start (`"Sun, 21 March 2027 00:00:00 GMT"`) to the event's
    `runStartDatesUtc`; it is a content-only change (the `lres` hash moves, no `SchemaVersion` bump).
    A track's `killPoints` is its **defeat-all base** (clients use it as the per-battle defeat-all points
    in per-unit points), not a kill score: it **must equal `defeatAll[0]`** (later `defeatAll` values may
    grow), and `defeatAll` must not be empty — enforced at load (`Validation/LreLanePointsValidation.cs`,
    `InvalidLaneKillPoints`). Example: `lres-votanuthar.json` once carried `tauFarsight`'s 34 / 26 / 17
    (copied from the wrong file) instead of 30 / 34 / 43, skewing every Uthar leaderboard figure.
    **Missions** (`regularMissions` / `premiumMissions`) stay V1's English lines in the raw file (V1's
    `questsToMissionText` output, so a refresh stays a copy) and are served as typed records
    `{ index, kind, count, faction, alliance, damageType, trait, text }` (raw order, `text` = the raw line,
    unused parameters `null`). `LreDenormalizer` matches each line against the ordered `LreMissionTemplates`
    (first match wins; the bare `"Defeat N <faction>"` fallback is last) and maps the captured label through an
    explicit alias table to the catalog id: factions → units/npcs `factionId` (`"AdMechs"` → `AdeptusMechanicus`,
    `"T'au"` → `Tau`, `"Death Guard"` → `DeathGuard`), alliances → `Imperial`/`Chaos`/`Xenos`, damage types →
    the units/npcs damage strings (`Bolter`, `Piercing`…), traits → `Data/curated/enums.json` trait ids (V1's labels:
    `"Big Target"` → `BigTarget`, `"Final Vengeance"` → `FinalJustice`, `"Deep Strike"` → `TeleportStrike`).
    `"Defeat N Daemon enemies"` (V1's `SlayEnemiesOfTrait`) is kind `defeatFaction` with `trait` `Daemon` and
    `faction` null. `Validation/LreMissionsValidation.cs` (`UnparsedMission`) fails the load on a line no
    template matches or a label with no alias, naming the event, file, list, index and line. **To add a mission
    template** (a new V1 quest task): add a `[GeneratedRegex]` with `count` (and at most one `label`) groups, a
    `LreMissionKinds` constant if the kind is new, and a `LreMissionTemplates` entry ahead of any broader
    pattern; then extend `LreMissionsTests` and bump `SchemaVersion` (a new `kind` is a contract change for the
    client, which must map it). A new label for an existing kind is just an alias-table row (content-only). A refresh must not reorder or shrink a live
    event's mission lists: plans store ticks as positional indices, so a reorder moves ticks onto other
    missions (a shrink only drops the ticks past the end, on read).
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
  A Home Screen Event definition with an entry in the raw `home-screen-event-scoring.json` gets it merged
  into its served `config` as `config.hse` (variants → tiers/milestones/repeat, trackers, modifiers; see
  `EventsDenormalizer.BuildEventDefinitions`). An occurrence resolves its variant by `hseVariant`, else
  `targetFactionId`, else `default`; validation fails the load when one resolves to nothing.

## Horde-mode tiers

`Data/curated/horde-mode-tiers.json` is a single hand-curated tier list (a one-element array) of how well a
character does in horde-style modes (many waves of weak enemies: Legendary Events today), served as
`horde-mode-tiers` with `characters` sorted by `unitId`. It is mode-agnostic on purpose; a consumer
decides how to use it. The apps' Legendary Event clear estimate multiplies combat power by each
character's multiplier and compares it with `lre-battles` `power` through a power ratio that lives in
the apps code (not in the catalog), so ratio changes ship as a web release.

- **`tiers`**: exactly `S`, `A`, `B`, `C`, `D` in that order, each `{ tier, multiplier }` with multiplier
  in (0, 5]. A character not listed is untiered ("not yet evaluated") and gets 1.0 (the baseline); the
  seeded tiers are all at or above it (S 1.90, A 1.70, B 1.45, C 1.20, D 1.00). D means "evaluated, weak
  in horde modes", so it shares the untiered 1.0 rather than penalising. Authoring ranges for a
  character's own multiplier (not validated): S 1.80–2.00, A 1.60–1.79, B 1.35–1.59, C 1.10–1.34,
  D 1.00–1.09.
- **`characters`** (`{ unitId, tier, multiplier? }`): `unitId` must be a character id (not a MoW or NPC),
  listed once; `tier` must be one of the tier names. Rank by summoning value, AoE / multi-hit and
  sustain. **`multiplier`** is an optional per-character override in (0, 5] that replaces the tier's
  value for that character; every seeded character sets one, to rank it within its tier. Put the
  evidence (estimate vs manual depth overrides) for a change in the PR description. It is omitted
  from the served JSON when not set.
- **`synergies`** (optional, on a character): up to 6 recommended teammates as character ids, best
  first, served in authoring order (the order ranks them, so it moves the hash); not the character
  itself, no duplicates; omitted when empty. Only ids: the strategy text (strengths, how to use, relic
  advice) is client locale content in the apps' `hordeStrategy` namespace.
- **`relics`** (optional, on a character): `{ equipmentId, multiplier }[]`, relic-conditional ratings.
  `equipmentId` must be a catalog relic (`isRelic`) whose `allowedUnits` includes the character, once
  per character; `multiplier` in (0, 5] and not below the character's base effective multiplier.
  Served sorted by `equipmentId`, omitted when empty. The client applies the highest one whose relic
  the synced roster has equipped (any level) in place of the base; the Tier List still ranks by base.
- Enforced at load (`Validation/HordeModeTiersValidation.cs`, `InvalidHordeModeTiers`). A value change
  only moves this dataset's hash; a new field bumps `SchemaVersion`.

## Unit power

`unit-power` is the single record of the power tables the in-game power formula reads (the apps compute
character, MoW and NPC power from it plus `characters`/`mows`, `npcs`, `equipment` and `upgrades`). It is
bound from the datamine `gameconfig/units/` files: `heroProgressionSteps` / `heroProgressionStepsMoW` (20 steps
each; served as `heroProgressionSteps` / `mowProgressionSteps` with a `step` index), `damageProfileModifiers`,
`traitPowerModifiers`, `abilityPowerModifiers` (`{baseMultiplier}`, served as `modifier`), and
`abilityPowerCurve` (`active`/`passive` by ability level − 1, `relic` by relic level − 1), plus each
`lineup.json` unit's `powerMultiplier` (percent, absent ⇒ 100) and, for characters, `upgrades` plus
`upgradesStatIncrease` (one row per rank, each value the increase of the upgrade id at the same position).

## Datamine source

`TacticusPlanner/tacticus-planner-data-mines` (private) extracts the game's config from every PC build into
`raw/` and tags each merged datamine `v<version>`. Its `docs/catalog-audit.md` maps each dataset here to its
datamine source; its `docs/roadmap.md` orders the migration. Datasets move onto it one change at a time.

- **`Data/datamine/REF`** — one line, `<tag> <40-hex sha>` (e.g. `v1.43.157.2989 e5c8…`). The SHA wins: a forced
  datamine re-run moves the tag. `local <path>` marks a copy from a working tree; tests reject it.
- **`Data/datamine/files.txt`** — the allowlist of `raw/`-relative paths (sorted, `#` comments). List a file
  only in the change whose denormalizer reads it. Copies are public (this repo is public); that was accepted.
- **Refresh** — `dotnet run scripts/pull-datamine.cs -- v<version>` (uses your git credentials), or
  `-- --source ../tacticus-planner-data-mines` while developing. It copies byte for byte, prunes unlisted
  copies, and rewrites `files.sha256` and `REF`. Never edit `Data/datamine/raw/**` by hand.
- **Gate** — `DatamineSourceTests` fails on a `local` or malformed pin, a missing or unlisted copy, and any
  checksum mismatch.
- **Migrating a dataset** — bind its denormalizer to `DatamineFiles.Open(...)`, delete the hand-kept raw file,
  and either keep the served shape (the manifest snapshot shows only real game-data hash moves, each explained
  in the PR) or rename to the game's terms (`SchemaVersion` bump plus the apps companion), never both in one
  change. Renamed ids that user data stores (battle ids, LRE ids, mission indices) need a data migration.
  Migrated: `equipment`, `character-ability-costs`, `mow-upgrade-costs`, `ascension-costs`, `unlock-shard-costs`,
  `upgrades`, `characters`, `mows`, `npcs`, `unit-power`, `raid-bosses` (`globalconfig/guildBoss/`; the
  game's unit sets are merged and must agree), `shops` (four merchants of `merchants.json`), `mythic-quests`
  (`mythic_{characterId}_{number}` quests of the `hero` group in `quests/groups.json`). Next, in order: HSE,
  `onslaught-rewards`, quest events/crates, LRE, `campaign-battles` last. A migrated dataset is refreshed by bumping `REF`, not by the V1 steps below.

## Updating to a new in-game version

**For datasets not yet migrated to the datamine there is no re-runnable pipeline.** The original raw data came
from one-off Python transforms that were removed; their `Data/**` files are the committed source of truth,
refreshed by hand against the V1 datamine. Bump `REF` (above) in the same refresh.

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

- **characters**, **mows**: now from the datamine (see **characters**, **mows** and **npcs** above); refresh by bumping `REF`.
- **mythic-quests**: now from the datamine (see above); refresh by bumping `REF`. Rewards stay the game's
  `"type[:amount]"` strings, parsed with a closed pattern set — a new reward kind fails startup until
  `MythicQuestRewardParser` learns it. Task text templates are **not** catalog data: they go to the apps
  `mythicQuestTasks` namespace (`apps/web/public/locales/en/`).
- **npcs**: now from the datamine (see above); refresh by bumping `REF`.
- **upgrades**: now from the datamine (see **upgrades** above); refresh by bumping `REF`.
- **unit-power**, **raid-bosses**, **shops**: now from the datamine (see above); refresh by bumping `REF`.
- **campaign-battles**: from V1 `campaign/data/new-battle-data.json`. The catalog shape is heavily
  denormalized (resolved enemy tables, rank labels) with no surviving transform — a full re-derivation is
  impractical; scope changes to id adds/removes and reward changes, and be wary that battle-id renames
  ripple into API tests + the player-data transformer.
- **LRE**: V1 migrated to a machine-generated format (`lre/data/{n}-{unit}.json` +
  `lre/data/new-le-battle-data.json` `legendaryEvents[]` + hand-kept `lre/data/lre-event-dates.ts`).
  `lre-event-dates.ts` holds **one** date per event (the next run, with no run number) and has gone stale
  (on 2026-10-16 it had Fabius Bile on 2026-11-11 and old Lysander/Farsight dates); use it only as a hint
  for where that date goes in `runStartDatesUtc` (its index is that run's number − 1, cross-checked against
  V1's `eventStage`), and take confirmed run dates from the maintainers. The
  catalog's raw `lres-*.json` shape mirrors V1's *older* hand-authored format, so derive it using the
  semantics in V1's `lre/data/new-format-adapter.ts` — `buildStaticLegendaryEvent`, `buildTrackName`
  (reconstructs `"Alpha (No Xenos)"` from `disallowedFactions`), `questsToMissionText` (task→string),
  `toLegacyPointsMilestones` / `toLegacyChestMilestones`. Wave enemies are `"{npcId}:{N}"` strings,
  where `N` is the 1-based position in the npc's stats ladder (not a star count; it reaches 56); aggregate
  identical `(id, N)` to `{id, ladderIndex: N, count}`. Do not range-check it: some boss entries point past
  their ladder and the client shows them as level unknown. `bonusObjectives` → `unitsRestrictions`
  (`{name, points, iconId, filter:{kind,target,exclude}, index}` — `iconId` is not served). Track
  `allowedUnitsFilter` derives from `disallowedFactions` (a full alliance roster ⇒
  `[{kind:Alliance, target:<alliance>, exclude:true}]`). Add/remove events by editing
  `GameCatalogDatasets.LreEvents` and adding/removing the `lres-{unit}.json` file. Every wave enemy id
  must already exist as an npc (add the npcs first).
- **client-side companion** (`tacticus-planner-apps`): new characters/equipment/traits need assets under
  `apps/web/public/game_catalog/` (portraits `characters/ui_image_RoundPortrait_{slug}_01.png` + full
  `ui_image_portrait_*`, equipment `equipment/ui_icon_item_{id}.png`, traits
  `traits/ui_icon_trait_{slug}_01.png`, relic abilities `abilities/ui_icon_ability2_{abilityId}.png`), an
  entry in `packages/game-catalog/src/game-entities/character-icon-overrides.ts` when the id→slug
  derivation diverges from the asset filename, and display names in
  `apps/web/public/locales/en/characters.json`. Source assets from
  `tacticusplanner/src/assets/images/snowprint_assets/`. **Asset filenames must match the catalog id
  exactly, including letter case** — V1's files sometimes differ in case (`DaemonFleshPlate` vs the id
  `DaemonfleshPlate`), which works on Windows and breaks on the case-sensitive host; rename on copy.
- **client-side game text** (`tacticus-planner-apps`): ability rules text (`npcAbilityText`, all four
  locales) is generated from the catalog's ability ids plus V1's ability table and localized descriptions.
  After any refresh that touches characters, npcs, mows or relic equipment, run
  `node apps/web/scripts/generate-ability-text-i18n.mjs` in the apps repo (it reads this repo's `Data/**`
  and V1 from the sibling checkouts); it fails when the uncovered-id set or a relic ability icon drifts
  from its known gaps. Commit the regenerated locale files with the refresh.

- **HSE scoring** (`Data/events/home-screen-event-scoring.json`): regenerate from V1's
  `src/fsd/4-entities/homescreen_events/data/*.json` with
  `python3 .agents/skills/game-catalog-data/scripts/import-hse-scoring.py ../tacticusplanner` (run from
  this repo's root). Its `SOURCES` table maps each V1 file to a definition id and variant key; add a row
  for a new V1 file (and the definition in `event-definitions.json` first), and give a new occurrence an
  `hseVariant` parameter when its ladder isn't the definition's `default`. `raidBattles` (campaign sweep)
  trackers are dropped; each one's modes are covered by a `killUnits` tracker with equal points. The script
  raises on V1 shapes it can't represent (empty allow lists, unknown tiers, per-tier tracker differences).

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
