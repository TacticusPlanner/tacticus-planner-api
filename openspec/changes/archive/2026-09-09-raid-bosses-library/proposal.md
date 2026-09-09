## Why

V2 has a `/library/raid-bosses` route, nav entry, and i18n keys already scaffolded, but the placeholder page says *"Raid Bosses are not available in the catalog yet"* because no raid-boss reference data exists anywhere in V2 — not in the served game catalog, not in `Data/**`, not in the client. V1's `learn/guildBosses` cluster (boss/prime list + detail) is backed by a single ~1.9 MB datamined `guild_boss.json`; to bring that page to V2 following the established id-only served-catalog pattern, the data has to be a first-class catalog dataset.

This is the backend half of a cross-repo pair. The companion is the `raid-bosses-library` change in `tacticus-planner-apps` (client-side consumption: IndexedDB store, query getter, list + detail pages), which this dataset unblocks. Per both repos' OpenSpec configs the API half applies first.

Scope is deliberately **list + detail** parity with V1: bosses, raid-boss primes, their stat progressions, weapons, ability/trait ids, and per-encounter modifiers. V1's separate guild-raid-season / tier-ladder view (`learn/guildBossReference`) is out of scope for this change.

## What Changes

- Add one authored raw dataset family under `Data/raid-bosses/**` ported from V1's `guild_boss.json`, covering: the season-config rotation, the keyed `unitSets` (bosses / primes / minions / field npcs — faction, movement, stat-progression ladder, weapons, active/passive/relic ability ids, trait ids), the keyed season configs (tiers → sets → encounters, each encounter referencing a unit id with its progression index, field npc ids, allowed/disallowed factions, and per-`hpLost` modifier ids), and the keyed modifier definitions (`{ type, target, subtarget(s), amount }`).
- Serve **one consolidated `raid-bosses` dataset** (denormalized, self-contained — encounters carry resolved unit-set and modifier data inlined, so the client never joins). Raw per-file sources are **not** served directly.
- The served dataset carries only structural/identity fields — unit-set ids, faction ids, ability ids, trait ids, npc ids, numeric stats, tier/set/encounter indices, modifier `{type,target,amount}`. **No** display names, no portrait/icon/wiki paths: the client resolves every boss/prime/ability/trait/faction name and image from its id, consistent with every other served catalog dataset.
- Extend catalog validation: every encounter `unitId` (minus its `:N` suffix) resolves to a served `unitSet`; every encounter modifier id and every season-config `unitSet` reference resolves; every `unitSet` ability/trait id is a non-empty string; every stat-progression ladder is non-empty.
- Register the new dataset key in the manifest / hashing pipeline and serve it via a new `AllowAnonymous` endpoint, following the existing pattern for the other served datasets. Update the Verify-guarded manifest snapshot test to include the new hash.
- Record the in-game `GameVersion` the raid-boss data was extracted from (mirrors the V1 datamine commit it was ported from).
- Purely additive: no existing served dataset's shape changes, no `SchemaVersion` bump, no EF Core migration (the game catalog is embedded JSON, not database state).

## Capabilities

### New Capabilities
- `raid-bosses-dataset`: the backend game catalog's consolidated raid-boss dataset — the raw authoring shape, the denormalized served projection (every field and type), the raw-vs-served split, cross-reference validation, and the additive manifest/version impact.

### Modified Capabilities
- none — this adds a new dataset to the existing game-catalog pipeline; no existing served dataset's requirements change.

## Impact

- **Data**: new `Data/raid-bosses/*.json` authored sources (season rotation + unit sets + season configs + modifier definitions), ported from `tacticusplanner` `src/fsd/4-entities/guild_boss/data/guild_boss.json` at a named game version.
- **Models**: new raw source key(s) and the served key `raid-bosses` in `Models/GameCatalogDatasets.cs`; new served-view records (`RaidBoss`, `RaidBossUnitSet`, `RaidBossStatProgression`, `RaidBossWeapon`, `RaidBossSeason`, `RaidBossTier`, `RaidBossEncounter`, `RaidBossEncounterModifier`); `Models/GameCatalogRelease.cs` `GameVersion` note.
- **Denormalization**: new `Denormalization/RaidBossDenormalizer.cs` building the consolidated served view (encounter → inlined unit-set/progression/modifier resolution; boss vs. prime classification from the `unitSets` key pattern; primarch flag from the raw `primarchs` list).
- **Validation**: `Validation/*.cs` extended with the raid-boss cross-reference checks; `ManifestValidation` requires `raid-bosses` non-empty.
- **Hashing / manifest**: `Utils/GameCatalogHashing.cs` registers `raid-bosses`; the manifest snapshot test (`GameCatalogSnapshotTests`) gains the new dataset hash.
- **Endpoints**: `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` gains one `AllowAnonymous` endpoint for `raid-bosses`; `artifacts/openapi` regenerates on build.
- No breaking change to any existing served dataset or the schema version; no database migration.
- **Companion**: `tacticus-planner-apps` change `raid-bosses-library` consumes this dataset.
