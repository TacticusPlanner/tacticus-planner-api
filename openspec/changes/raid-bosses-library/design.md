## Context

See `proposal.md` for motivation. Relevant current state (see this repo's `game-catalog-data` skill for the full pipeline):

- Raw sources live under `Data/**`, discovered by convention (dataset key `foo-bar` → embedded `foo-bar.json`, matched by leaf filename; subfolders don't matter). `Models/GameCatalogDatasets.cs` is the registry of raw source keys and the `Served` list.
- `Denormalization/*.cs` is a `partial class GameCatalogDenormalizer`, one file per entity, building served views from raw collections. `Validation/*.cs` is a `partial class GameCatalogValidator` running over the **raw** snapshot at load, throwing on any error (fail fast, no partial catalog).
- Every served dataset today carries only structural/identity fields — no `icon`/`iconId`/`wikiLink` anywhere. Raid bosses follow the same rule.
- `GameCatalogRelease.SchemaVersion` is bumped only on a breaking shape change to an *existing* served dataset; adding a new dataset key is not that. `GameVersion` records the in-game version the embedded data was extracted from.
- The catalog is embedded JSON loaded at startup — there is **no database state**, so no EF Core migration is in scope for this change.
- Source of truth for the raw content: `tacticusplanner` (`develop`), `src/fsd/4-entities/guild_boss/data/guild_boss.json` (~1.9 MB) plus the derivation logic in `guild-boss.service.ts` / `guild-boss-modifiers.ts`. Top-level keys: `guildBossSeasonConfigRotation`, `unitSets` (98 keyed entries), `guildBossSeasonDataConfigsGDTO` (keyed season configs), `modifiers` (70 keyed defs), `primarchs` (list of prime unit-set ids). `misc` and `boards` are not needed by the list + detail scope.

## Goals / Non-Goals

**Goals:**
- Land one consolidated, self-contained served `raid-bosses` dataset inside the existing pipeline shape, with no new architectural pattern.
- Do the boss/prime classification, `:N`-suffix splitting, and encounter-modifier resolution **once**, at denormalization, so the client consumes a flat, join-free structure.
- Make every raid-boss cross-reference (encounter → unit set, encounter → modifier def, season → unit set) a build-time validation failure, consistent with the rest of the pipeline.

**Non-Goals:**
- Any client-side work (IndexedDB store, query getter, list/detail pages, i18n, icon mapping) — that is the companion `raid-bosses-library` change in `tacticus-planner-apps`.
- V1's guild-raid-season / tier-ladder reference view (`learn/guildBossReference`) — out of scope; the `seasons` collection is served so a later change can build it without another API change.
- Porting V1's runtime modifier *math* (`computeStatAdjustments`, `applyAbilityAdjustments`, …). The API serves the modifier **definitions** inlined per encounter; applying them to stats/abilities is the client's job, same as V1 does it in the browser.
- An authoring UI for the raw files — hand-edited JSON, same as every other catalog source.
- Serving portraits/round-icons. The client maps unit-set / npc / ability / trait / faction ids to assets (V1's `guild-boss-portraits.ts` map is client-side reference material for the companion change).

## Decisions

**1. One consolidated served dataset (`raid-bosses`), not four raw datasets served uniformly.**
The client's list + detail page needs bosses, primes, their progression ladders, weapons, ability/trait ids, and — per encounter — the scaled modifier definitions. All of that is a join across `unitSets` + `guildBossSeasonDataConfigsGDTO` + `modifiers`. Doing the join server-side yields a structure the client renders directly; serving the raw pieces would push the datamine's shape and three cross-collection joins onto every consumer.
- _Alternative considered_: serve `raid-boss-units` and `raid-boss-seasons` as two datasets (mirroring `campaign-battles` / `campaign-definitions`). Rejected for now — unlike battles, the unit sets are small (98 entries) and always needed alongside the seasons for detail; one dataset keeps one key to hash/version. Revisit only if payload size becomes a sync concern.

**2. Canonical served structure: `{ seasonConfigRotation, bosses[], primes[], seasons{} }` with encounters as the only place season/unit data meets.**
`bosses[]` / `primes[]` are the unit catalog (identity + progression + weapons + ability/trait ids + `isPrimarch`). `seasons{}` is the tier/set/encounter tree. An `encounter` carries `unitSetId` + `progressionIndex` (a reference into `bosses`/`primes`) and `modifiers[]` with the resolved definition **inlined**. Nothing else is duplicated: the client indexes `bosses`/`primes` by id and resolves an encounter's boss by `unitSetId`, then picks the stat step by `progressionIndex`. Summaries (e.g. "max known progression") are derived by the client from `statProgression.length`, not a separate served field.

**3. Boss vs. prime classification is derived from the raw `unitSet` key pattern at denormalization, and asserted.**
`^GuildBoss(\d+)Boss` → boss; `^GuildBoss(\d+)(?:MiniBoss|Minion)(\d+)` → prime (ported from V1's `BOSS_SET_RE` / `PRIME_SET_RE`). Keys matching neither (field npcs like `GuildBoss1Npc1…`, loot objects) are not top-level entries but remain resolvable for `encounter.fieldNpcIds`. Ordering: bosses by `\d+` ascending; primes by `(bossNumber, primeIndex)` ascending. If a `unitSet` key matches neither pattern **and** is referenced by an encounter as its main `unitId`, that's a validation failure (unexpected shape), not a silent drop.

**4. The `:N` progression suffix on encounter `unitId`s is split into `unitSetId` + `progressionIndex` server-side; the raw 1-based index is kept as-is.**
V1 stores encounter unit references as `"<unitSetId>:<1-basedIndex>"` and does the split in `getUnitSetId` / `getProgressionIndexFromUnitId` (absent suffix → `1`). Serving the split form means the client never string-parses ids. The index stays 1-based as authored; the client subtracts 1 when indexing `statProgression` (documented in the companion spec), matching V1's `encounterStatsIndex`.

**5. Encounter modifiers are served with the modifier definition inlined; the raw `modifiers` map is not served.**
An encounter modifier is `{ hpLost, modifier: "<id>" }` in the raw data; the definition (`{ type, target, subtarget(s), amount }`) lives in the top-level `modifiers` map. The client always needs both together, so denormalization resolves the id and inlines `{ modifierId, type, target, subtarget|subtargets, amount }` alongside `hpLost`. An unresolved id fails the build. The standalone `modifiers` map is not a served collection — every use of it is already inlined.
- _Alternative considered_: serve `modifiers` as its own keyed collection and keep encounters carrying just the id. Rejected — it re-introduces a client-side join for zero benefit; the definitions are tiny and fully consumed by inlining.

**6. Non-nullable numeric stats; genuinely-optional fields stay optional.**
Per the pipeline convention, `health`/`damage`/`fixedArmor`/`rank`/`starLevel`/`progressionIndex`/`abilityLevel` are non-nullable (missing source value → `0`). `blockChance`/`blockDamage`/`critChance`/`critDamage`, `weapons[].range`, `bossType`, `disallowedFactionIds`, and the ability/trait arrays are present only when the source has them.

**7. Additive release: no `SchemaVersion` bump, no migration; `GameVersion` records the datamine version.**
Adding `raid-bosses` changes no existing served shape. The manifest gains one hash entry; the Verify-guarded `GameCatalogSnapshotTests` snapshot is re-approved to include it. `GameVersion` in `GameCatalogRelease.cs` is set to the in-game version the ported `guild_boss.json` corresponds to (trace the V1 datamine commit — messages like `"1.41 from the apk"`).

## Risks / Trade-offs

- [Trade-off] The consolidated dataset embeds every boss's full progression ladder and every season's encounter tree in one payload. Estimated well under the raw 1.9 MB after dropping `misc`/`boards` and display-only fields, but larger than most single datasets. Accepted — it's downloaded once and cached in IndexedDB, and splitting it (Decision 1 alternative) trades size for a client-side join. Revisit if the manifest diff shows it dominating sync cost.
- [Risk] V1's datamine has regressed fidelity in places post-1.41 (dropped zero-valued fields, unpopulated ability arrays). Porting `guild_boss.json` verbatim could import a downgrade. → Mitigation: reconcile per the `game-catalog-data` skill — take genuine game changes, write missing numerics as `0`, don't blindly overwrite.
- [Risk] The boss/prime key-pattern regexes (Decision 3) are datamine conventions, not a guaranteed contract. A future patch could introduce a boss whose key doesn't match `GuildBoss\d+Boss…`. → Mitigation: an encounter's main `unitId` not resolving to a classified entry is a build failure, so a new shape surfaces loudly at the next catalog refresh rather than silently omitting a boss.
- [Risk] `encounterType` values seen in the raw data are `Boss` and `Crystal`; an unseen third value would be carried through. → Mitigation: validate the enum at load (fail on an unrecognized value), same failure mode as the events-calendar recurrence-kind check.
- [Trade-off] Serving `seasons` (tiers/sets) now, before the client uses it (list + detail only need `bosses`/`primes` + encounters for field enemies and modifiers). Accepted deliberately — it's the same denormalization pass and it lets the later season-view change be client-only, with no second API round.

## Migration Plan

Purely additive: one new dataset key, no changes to any existing dataset's shape or `SchemaVersion`, no database migration (embedded catalog). Ships as a normal catalog content release — per-dataset manifest hashing means existing clients unaware of `raid-bosses` are unaffected; the companion `tacticus-planner-apps` `raid-bosses-library` change picks it up through the existing manifest-diff sync once it lands. Rollback is a plain revert; no data backfill in either direction. Apply order: this API change first, then the apps companion.

**Companion change:** `raid-bosses-library` in `tacticus-planner-apps`. **Shared contract surface:** the served `raid-bosses` dataset (key, envelope, and the `{ seasonConfigRotation, bosses[], primes[], seasons{} }` projection shape defined in `specs/raid-bosses-dataset/spec.md`).
