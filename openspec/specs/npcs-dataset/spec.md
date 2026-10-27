## Purpose

Provides the backend game catalog's consolidated non-playable-unit reference data — every NPC variation the game spawns, its faction and alliance, its classification as a regular unit, Machine of War, or loot object, its weapons, ability and trait ids, and its stat ladder — so a client can render an NPC library and per-unit detail view without joining the raw per-faction sources or classifying units by id pattern.

## Requirements

### Requirement: The catalog serves one flat `npcs` dataset built from the per-faction raw sources

The game catalog SHALL serve a dataset keyed `npcs` built from the pinned datamine copy `gameconfig/units/npc.json` (keyed by NPC id), with damage profiles from `gameconfig/units/abilities.json`. The catalog SHALL keep no hand-kept NPC files. NPCs SHALL be grouped per faction under the source key `npcs-<faction>` (lowercased `FactionId`); records without a `FactionId` (loot objects) form the `npcs-objects` bucket. The served dataset SHALL be a single flat list of NPC records ordered by source key (ordinal) and then by id (ordinal). The raw file SHALL NOT be served directly. The dataset SHALL be served without authentication and SHALL appear in the catalog manifest with its own content hash.

#### Scenario: Records from every raw source are present

- **WHEN** the datamine `npc.json` at `v1.43.157.2989` holds 539 NPCs across 20 factions plus loot objects
- **THEN** every one appears exactly once in the served list, in 21 source groups

### Requirement: Each NPC record carries the served projection shape

Every record in the `npcs` dataset SHALL carry exactly these fields:

| Field | Type | Source |
|---|---|---|
| `id` | string | the `npc.json` key — the unique variation id (e.g. `necroBossWardenLHE`) |
| `name` | string | `name` — the game's untranslated unit name; clients SHALL resolve the localized display name from `id`, not from this field |
| `factionId` | string | `FactionId` (`Objects` when absent) |
| `alliance` | string | `GrandAllianceId` (`Neutral` when absent) |
| `kind` | `unit` \| `machineOfWar` \| `object` | derived — see the classification requirement |
| `meleeDamage` | string | `weapons[0].DamageProfile`; `""` when the unit has no weapon |
| `meleeHits` | integer | `weapons[0].hits`; `0` when the unit has no weapon |
| `rangedDamage` | string \| null | `weapons[1].DamageProfile`; `null` when the unit has no ranged weapon |
| `rangedHits` | integer \| null | `weapons[1].hits`; `null` when the unit has no ranged weapon |
| `distance` | integer \| null | `weapons[1].Range`; `null` when the unit has no ranged weapon |
| `movement` | integer | `Movement`; a missing value is written as `0` |
| `traits` | string[] | `traits` in game order, without the `Hero` trait every unit carries |
| `activeAbilities`, `passiveAbilities` | string[] | ability ids in game order |
| `activeAbilityDamage`, `passiveAbilityDamage` | string[] | the distinct `damageProfile` / `damageProfile_N` constants of those abilities in `abilities.json`, sorted ordinally |
| `stats` | stat[] | `stats` rows in game order; each row `{ abilityLevel, damage, armour, health, progressionIndex, rank, stars }` from `AbilityLevel`, `Damage`, `FixedArmor`, `Health`, `ProgressionIndex`, `Rank`, `StarLevel`, all non-nullable integers (a missing numeric is written as `0`) |

No record SHALL include an icon path, portrait id, wiki link, or any other presentational field; the client derives portraits and localized names from `id`, faction display from `factionId`, and trait / damage-type display from their ids.

#### Scenario: Faction and alliance are stamped from the owning source file

- **WHEN** the datamine NPC `necroBossWarden` has `FactionId` `Necrons` and `GrandAllianceId` `Xenos`
- **THEN** the served record `necroBossWarden` has `factionId` `Necrons` and `alliance` `Xenos`

#### Scenario: Loot objects carry the objects faction

- **WHEN** the datamine NPC `LootObj_AmmoBox` has no `FactionId` or `GrandAllianceId`
- **THEN** the served record `LootObj_AmmoBox` has `factionId` `Objects` and `alliance` `Neutral`

#### Scenario: Stat rows round-trip in source order

- **WHEN** a datamine NPC lists stat rows whose first row is `Rank` 2 / `StarLevel` 2 and whose second row is `Rank` 1 / `StarLevel` 2
- **THEN** the served `stats` array has those rows in that same order; the server does not sort or de-duplicate them

#### Scenario: Served payload carries no presentational field

- **WHEN** a datamine NPC carries a `visualId`
- **THEN** the served record does not include it or any other presentational field

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

### Requirement: An unreadable NPC fails startup

Loading SHALL fail, naming the dataset and the NPC, when an NPC's first weapon has a range, it has more than two weapons, or it names an ability `abilities.json` does not define.

#### Scenario: Unknown ability

- **GIVEN** an NPC whose `activeAbilities` holds an id missing from `abilities.json`
- **WHEN** the catalog loads
- **THEN** loading fails naming `npcs`, the NPC and the ability
