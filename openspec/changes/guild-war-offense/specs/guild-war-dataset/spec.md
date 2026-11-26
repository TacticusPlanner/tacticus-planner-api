## Purpose

Defines the served `guild-war` catalog dataset: the game's Guild War config per season, which Guild War Offense reads for zones, slot rarity caps, buffs and maps, and which Counters, Defense and Zones build on later.

## ADDED Requirements

### Requirement: The guild-war dataset serves one record per live season

The catalog SHALL serve `guild-war` at `/api/v1/game-catalog/guild-war` in the standard dataset envelope and list it in the manifest. It SHALL be built from the datamine `globalconfig/guildWar.json` and SHALL hold one record per entry of `guildWarSeasonConfigs` whose key matches `guildWarSeason[A-Z]` exactly, ordered by that key. Test configs (`_staging`, `_fast`, `_ci2` and any other suffix) SHALL NOT be served. The dataset is not time-dependent: the server SHALL NOT say which season is live.

Each record SHALL carry exactly:

- `id` (string): the season key, e.g. `guildWarSeasonA`
- `nextSeasonId` (string): the game's `nextGuildWarSeasonConfigId`
- `maxAttempts` (integer): attacks a player has per war
- `zones` (array, layout order): each `{ id (string), visualId (string), buffId (string or null), canBeMoved (boolean), score (integer), row (integer), column (integer) }`
- `tiers` (array, lowest first): each `{ id (string), rarityCaps (string array of 5) }`
- `levels` (array, `battlefieldLevel` ascending): each `{ battlefieldLevel (integer), attemptDebuffPercent (integer), scoreFactorPercent (integer or null), zones (array of { zoneId (string), tierId (string) }, in the record's zone order) }`
- `buffs` (array, ordered by id): each `{ id (string), scope (string), abilities (array of { abilityId (string), rarity (string or null), variables (object of string to integer array) }) }`
- `maps` (array, game order): each `{ boardId (string), spawnPointsSet (integer) }`
- `scoring`: `{ unitKilled (integer), lineupEliminated (array of { maxAttempts (integer), maxUnitsLost (integer), score (integer) }, game order) }`
- `powerCaps` (array, rarity order): each `{ rarity (string), maxPower (integer) }`

The server SHALL send ids and structured values only. Zone, tier, buff and map names and icons are the client's.

#### Scenario: Test seasons are dropped

- **GIVEN** the datamine lists `guildWarSeasonA` to `D`, `guildWarSeasonA_staging` and `guildWarSeasonA_ci2`
- **WHEN** the dataset is read
- **THEN** it has exactly four records, `guildWarSeasonA` to `guildWarSeasonD`

#### Scenario: Seasons keep their own zones

- **WHEN** the `guildWarSeasonD` record is read
- **THEN** its zones include `ComsStation`, `AntiAirBattery1` and `AntiAirBattery2` and not `LandingPad1`, while `guildWarSeasonA` has `LandingPad1` and `AntiAirBattery`

### Requirement: The guild-war record is derived from the season config

Each served field SHALL be derived from the season config as follows:

| Served | Derived from |
| --- | --- |
| `zones` | `defaultZoneTypeLayout` read row by row (row 0 first), left to right; `row` and `column` are the 0-based positions; `visualId`, `canBeMoved` and `buffId` from `zoneTypeConfigs[id]` (`buffId` absent ⇒ null); `score` from `scorePerZoneType[id]` |
| `tiers` | `zoneTiers`: `zoneTierId` and `rarityCaps`, ordered by the rarity of `rarityCaps[0]` ascending (Trooper, Veteran, Elite, Hero today) |
| `levels` | `levels`: `battlefieldLevel`, `attemptDebuffPercent` (absent ⇒ 0), `scoreFactorPercent` (absent ⇒ null, as on level 0), and each zone's `zoneTierId`, listed in the record's zone order |
| `buffs` | `buffs`: the key as `id`, `scope`; `abilities` are the `abilities` entries keyed exactly `abilityId` (served with `rarity` null) or `{abilityId}_{Rarity}` for a game rarity (served with that rarity), in rarity order after the unsuffixed one; `variables` as shipped; `constants` are not served |
| `maps` | `mapPool`: `boardId`, `spawnPointsSet` |
| `scoring` | `scoreFromUnitKilled`; `scoreForLineupEliminated` |
| `powerCaps` | the top-level `guildWarConfig.maxHeroPowerCap`, the same on every record, in rarity order |

The load SHALL fail when a layout zone has no `zoneTypeConfigs` or `scorePerZoneType` entry, a level does not list every layout zone exactly once, a level names an unknown tier, a tier does not have exactly 5 rarity caps of game rarities, a zone's `buffId` is not a key of `buffs`, a buff has no matching ability, `nextSeasonId` is not a served season, or `maxAttempts` is not positive.

#### Scenario: A zone's slot caps

- **GIVEN** `guildWarSeasonA` at battlefield level 5
- **WHEN** a client reads zone `Armoury`
- **THEN** its level-5 tier is `Elite`, whose `rarityCaps` are `Legendary`, `Legendary`, `Epic`, `Epic`, `Epic`

#### Scenario: Rarity-keyed buff abilities

- **WHEN** buff `EnvAngelsOfDeath_Regional` is read
- **THEN** its abilities are `EnvAngelsOfDeath_Common` to `EnvAngelsOfDeath_Mythic`, each with its rarity, in rarity order

#### Scenario: Level 0 has no score factor

- **WHEN** level 0 of any season is read
- **THEN** `scoreFactorPercent` is null and `attemptDebuffPercent` is 0

### Requirement: The calendar names each season's config

A `guild-war-season` occurrence in the events calendar MAY carry the parameter `seasonConfigId`, the id of the `guild-war` season that Guild War season runs. When present it SHALL name a served season, else the catalog load SHALL fail with `UnknownGuildWarSeasonConfig`. Clients SHALL default a new war to the config of the current, or else the latest started, season that names one, and to the first served season when none does.

#### Scenario: Season 29 runs C

- **WHEN** the calendar occurrence for season 29 is read
- **THEN** its `seasonConfigId` is `guildWarSeasonC`
