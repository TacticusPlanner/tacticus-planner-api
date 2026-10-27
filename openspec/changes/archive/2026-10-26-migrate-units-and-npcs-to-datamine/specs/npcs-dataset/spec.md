## MODIFIED Requirements

### Requirement: The catalog serves one flat `npcs` dataset built from the per-faction raw sources

The game catalog SHALL serve a dataset keyed `npcs` built from the pinned datamine copy `gameconfig/units/npc.json` (keyed by NPC id), with damage profiles from `gameconfig/units/abilities.json`. The catalog SHALL keep no hand-kept NPC files. NPCs SHALL be grouped per faction under the source key `npcs-<faction>` (lowercased `FactionId`); records without a `FactionId` (loot objects) form the `npcs-objects` bucket. The served dataset SHALL be a single flat list of NPC records ordered by source key (ordinal) and then by id (ordinal). The raw file SHALL NOT be served directly. The dataset SHALL be served without authentication and SHALL appear in the catalog manifest with its own content hash.

#### Scenario: Npcs dataset is served without authentication

- **WHEN** a client requests the `npcs` endpoint
- **THEN** the data is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: Npcs dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `npcs`

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

## ADDED Requirements

### Requirement: An unreadable NPC fails startup

Loading SHALL fail, naming the dataset and the NPC, when an NPC's first weapon has a range, it has more than two weapons, or it names an ability `abilities.json` does not define.

#### Scenario: Unknown ability

- **GIVEN** an NPC whose `activeAbilities` holds an id missing from `abilities.json`
- **WHEN** the catalog loads
- **THEN** loading fails naming `npcs`, the NPC and the ability
