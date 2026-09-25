## Purpose

Provides the backend game catalog's consolidated non-playable-unit reference data — every NPC variation the game spawns, its faction and alliance, its classification as a regular unit, Machine of War, or loot object, its weapons, ability and trait ids, and its stat ladder — so a client can render an NPC library and per-unit detail view without joining the raw per-faction sources or classifying units by id pattern.

## Requirements

### Requirement: The catalog serves one flat `npcs` dataset built from the per-faction raw sources

The game catalog SHALL serve a dataset keyed `npcs` built from the raw per-faction NPC source files (`npcs-<faction>.json`, plus the faction-less `npcs-objects.json`). The served dataset SHALL be a single flat list of NPC records ordered by raw source key (ordinal) and then raw source order within a file. The raw per-faction files SHALL NOT be served directly. The dataset SHALL be served without authentication and SHALL appear in the catalog manifest with its own content hash.

#### Scenario: Npcs dataset is served without authentication

- **WHEN** a client requests the `npcs` endpoint
- **THEN** the data is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: Npcs dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `npcs`

#### Scenario: Records from every raw source are present

- **WHEN** the raw sources contain 20 faction files and one objects file
- **THEN** every NPC record from all 21 files appears exactly once in the served list

### Requirement: Each NPC record carries the served projection shape

Every record in the `npcs` dataset SHALL carry exactly these fields:

| Field | Type | Source |
|---|---|---|
| `id` | string | raw `id` — the unique variation id (e.g. `necroBossWardenLHE`) |
| `name` | string | raw `name` — the datamine unit name, retained unchanged from the existing dataset; clients SHALL resolve the localized display name from `id`, not from this field |
| `factionId` | string | the `factionId` of the raw file the record was loaded from (e.g. `Necrons`; `Objects` for `npcs-objects.json`) |
| `alliance` | string | the `alliance` of the raw file the record was loaded from (e.g. `Xenos`; `Neutral` for `npcs-objects.json`) |
| `kind` | `unit` \| `machineOfWar` \| `object` | derived — see the classification requirement |
| `meleeDamage` | string | raw damage-profile id |
| `meleeHits` | integer | raw |
| `rangedDamage` | string \| null | raw; `null` when the unit has no ranged weapon |
| `rangedHits` | integer \| null | raw; `null` when the unit has no ranged weapon |
| `distance` | integer \| null | raw ranged distance; `null` when the unit has no ranged weapon |
| `movement` | integer | raw; a missing value in the source is written as `0` |
| `traits` | string[] | raw trait ids in source order |
| `activeAbilities`, `passiveAbilities` | string[] | raw ability ids in source order |
| `activeAbilityDamage`, `passiveAbilityDamage` | string[] | raw damage-profile ids in source order |
| `stats` | stat[] | raw stat rows in source order; each row `{ abilityLevel, damage, armour, health, progressionIndex, rank, stars }`, all non-nullable integers (a missing numeric is written as `0`) |

No record SHALL include an icon path, portrait id, wiki link, or any other presentational field; the client derives portraits and localized names from `id`, faction display from `factionId`, and trait / damage-type display from their ids.

#### Scenario: Faction and alliance are stamped from the owning source file

- **WHEN** `npcs-necrons.json` declares `factionId` `Necrons` and `alliance` `Xenos` and contains the record `necroBossWarden`
- **THEN** the served record `necroBossWarden` has `factionId` `Necrons` and `alliance` `Xenos`

#### Scenario: Loot objects carry the objects faction

- **WHEN** `npcs-objects.json` declares `factionId` `Objects` and `alliance` `Neutral` and contains the record `LootObj_AmmoBox`
- **THEN** the served record `LootObj_AmmoBox` has `factionId` `Objects` and `alliance` `Neutral`

#### Scenario: Stat rows round-trip in source order

- **WHEN** a raw NPC lists 7 stat rows whose first row is `rank` 2 / `stars` 2 and whose second row is `rank` 1 / `stars` 2
- **THEN** the served `stats` array has 7 rows in that same order; the server does not sort or de-duplicate them

#### Scenario: Served payload carries no presentational field

- **WHEN** a raw NPC record carries an `icon` path
- **THEN** the served record does not include `icon` or any other presentational field

### Requirement: Each NPC record is classified as a unit, Machine of War, or loot object

The denormalization SHALL derive `kind` for every record as follows, evaluated in this order:

1. `object` — the record was loaded from `npcs-objects.json`.
2. `machineOfWar` — the record's raw `traits` contains the trait id `MachineOfWar`.
3. `unit` — every other record.

The classification SHALL NOT depend on the spelling of the record's `id`. The catalog build SHALL fail if a record loaded from `npcs-objects.json` carries the `MachineOfWar` trait, since the two classifications are mutually exclusive by source.

#### Scenario: Machine of War classified by trait regardless of id casing

- **WHEN** the raw records `deathNpcMoWCrawler` and `astraNpcMowOrdnanceBattery` each carry the trait `MachineOfWar`
- **THEN** both served records have `kind` `machineOfWar`

#### Scenario: Objects classified by source file

- **WHEN** a record is loaded from `npcs-objects.json`
- **THEN** its served `kind` is `object`, whether or not it carries the `Object` trait

#### Scenario: Regular unit

- **WHEN** the raw record `necroNpc1Warrior` is loaded from `npcs-necrons.json` and its traits do not include `MachineOfWar`
- **THEN** its served `kind` is `unit`

#### Scenario: Zero-stat units are still served as units

- **WHEN** a raw record such as `genesDecoy` is loaded from a faction file, lacks the `MachineOfWar` trait, and every stat row is all zeros
- **THEN** it is served with `kind` `unit` and its zero stat rows unchanged; hiding it is a client presentation decision

### Requirement: Adding the classification and faction fields does not change the dataset's identity or schema version

The change SHALL keep the dataset key `npcs`, its endpoint, and its envelope unchanged, and SHALL NOT bump the catalog `schemaVersion`, because every existing field keeps its name, type, and order semantics and the new fields are additive. The `npcs` dataset content hash SHALL change, and the manifest SHALL reflect the new hash.

#### Scenario: Existing consumers keep working

- **WHEN** a client built against the previous `npcs` shape reads the new payload
- **THEN** every field it previously read is present with the same type and meaning

#### Scenario: Manifest hash reflects the new shape

- **WHEN** the game catalog manifest is requested after this change
- **THEN** the `npcs` entry's hash differs from the pre-change hash while `schemaVersion` is unchanged
