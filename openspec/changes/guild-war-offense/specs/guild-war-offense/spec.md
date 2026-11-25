## Purpose

Defines a player's Guild War Offense record: their wars and the attacks they log in each, the endpoints that read and write them, the rules a logged attack follows, and their lifecycle with the profile.

## ADDED Requirements

### Requirement: A war belongs to one profile and keeps its attacks

A war SHALL belong to exactly one profile and SHALL be readable and writable only by that profile. A profile SHALL have at most one current war (one that has not ended). Every war endpoint SHALL return a war as:

- `id` (string): a stable UUID
- `revision` (integer)
- `seasonId` (string): a `guild-war` season id
- `battlefieldLevel` (integer, 0 to 5)
- `startedAt` (date-time)
- `endedAt` (date-time or null): null for the current war
- `tokensUsed` (integer): the number of attacks
- `deployedUnitIds` (string array): every unit of every attack, each attack's characters then its MoW, in attack order
- `attacks` (array): the attacks, oldest `attackedAt` first, the id as the tiebreak

An attack SHALL be returned as:

- `id` (string), `revision` (integer)
- `attackedAt` (date-time): when it was logged
- `zoneId` (string), `slot` (integer, 0 to 4)
- `rarityCap` (string): the rarity cap of that zone and slot when it was logged
- `characters` (array of 1 to 5 lineup units) and `mow` (lineup unit or null): each `{ unitId (string), progression (string), rank (string or null), xpLevel (integer), abilityLevels (integer array of 0 to 2) }`; `rank` is null for a MoW
- `result` (`won`, `lost` or null) and `unitsLost` (integer 0 to 6, or null): null when not recorded
- `enemy` (object or null): `{ characters (array of 0 to 5 enemy units), mow (enemy unit or null), buffIds (string array), boardId (string or null) }`, each enemy unit `{ unitId (string), rarity (string or null), rank (string or null), state (`killed`, `damaged` or null) }`

Ids and codes only: the server SHALL NOT send display names or icons. Attacks of one profile SHALL NOT be readable by another, and SHALL NOT be pooled or shared.

#### Scenario: Another profile's war

- **GIVEN** a war owned by another profile
- **WHEN** the caller updates or deletes one of its attacks
- **THEN** the API responds `404`

#### Scenario: Derived tokens and deployed units

- **GIVEN** a war with two attacks, the first with characters `a`, `b` and MoW `m`, the second with characters `c`, `d`, `e`
- **WHEN** the war is read
- **THEN** `tokensUsed` is 2 and `deployedUnitIds` is `a`, `b`, `m`, `c`, `d`, `e`

### Requirement: The current war is read, set and replaced through the war endpoints

`GET /api/v1/guild-wars/current` SHALL return `{ war }`, with `war` null when the profile has no current war. `PUT /api/v1/guild-wars/current` with `{ seasonId, battlefieldLevel, revision }` SHALL create the current war when there is none (`revision` ignored, `startedAt` now) and otherwise SHALL update its season and battlefield level when `revision` matches, else respond `409` `guildWarRevisionStale` with the current war. `POST /api/v1/guild-wars` with `{ seasonId, battlefieldLevel }` SHALL end the current war (`endedAt` now), keeping it and its attacks, and create a new current war with no attacks. Each SHALL return the war as `GET` returns it. A `seasonId` that is not a served season, or a level that is not one of its levels, SHALL be refused with `400` `guildWarUnknownSeason` or `guildWarUnknownLevel`. Changing the battlefield level SHALL NOT change the `rarityCap` of attacks already logged.

#### Scenario: No war yet

- **GIVEN** a profile that never started a war
- **WHEN** it reads the current war
- **THEN** the API responds `200` with `war` null

#### Scenario: Starting a new war keeps the old one

- **GIVEN** a current war with 3 attacks
- **WHEN** the caller starts a new war
- **THEN** the response is a war with no attacks and `endedAt` null, and the past wars list starts with the old war, its `endedAt` set and its 3 attacks kept

### Requirement: Past wars are listed newest first

`GET /api/v1/guild-wars` SHALL return `{ items, nextAfter }`: the profile's ended wars, newest `startedAt` first with the id as the tiebreak, each with its attacks, at most `limit` (1 to 50, default 20) per page. `nextAfter` SHALL be an opaque cursor for the following page, or null on the last page; passing it as `after` SHALL continue the list. The current war SHALL NOT be listed.

#### Scenario: Paging

- **GIVEN** 3 ended wars
- **WHEN** the caller lists with `limit` 2, then with `after` set to the returned `nextAfter`
- **THEN** the first page holds the two newest wars, the second the oldest, and its `nextAfter` is null

### Requirement: Attacks are logged, edited and undone

`POST /api/v1/guild-wars/current/attacks` SHALL add an attack to the current war, or respond `409` `guildWarNotStarted` when there is none. `PUT /api/v1/guild-wars/{warId}/attacks/{attackId}` SHALL replace an attack's fields when `revision` matches, else respond `409` `guildWarAttackRevisionStale` with the current war. `DELETE /api/v1/guild-wars/{warId}/attacks/{attackId}` SHALL remove the attack (Undo). An attack of an ended war MAY be edited or deleted. Each SHALL return the attack's war as `GET` returns it. Create and update SHALL send `zoneId`, `slot`, `characters`, `mow`, `result`, `unitsLost` and `enemy`; create MAY send `attackedAt` (default now), and update keeps it.

#### Scenario: Undo frees the units

- **GIVEN** an attack with character `a` in the current war
- **WHEN** the caller deletes it
- **THEN** the war's `tokensUsed` drops by one and `deployedUnitIds` no longer holds `a`

#### Scenario: Stale attack edit

- **GIVEN** an attack at revision 2
- **WHEN** a client updates it with revision 1
- **THEN** the API responds `409` `guildWarAttackRevisionStale` with the current war, and nothing changes

### Requirement: A logged attack follows the war's rules

The server SHALL check every created or updated attack and SHALL refuse it with `400` and the first code that applies:

| Code | When |
| --- | --- |
| `guildWarUnknownZone` | `zoneId` is not a zone of the war's season, or `slot` is outside 0 to 4 |
| `guildWarUnknownUnit` | a lineup or enemy `characters` id is not a catalog character, a `mow` id is not a catalog MoW, a unit is listed twice in the lineup or twice in the enemy lineup, or an enemy `buffIds` entry or `boardId` is not one of the season's buffs or maps |
| `guildWarUnitDeployed` | a lineup unit is in another attack of the same war |
| `guildWarTokensSpent` | (create only) the war already has the season's `maxAttempts` attacks, or 10 when the season is no longer served |

On create and on an update that changes the zone or slot, the server SHALL store as `rarityCap` the `rarityCaps[slot]` of the zone's tier at the war's battlefield level; an update that keeps them SHALL keep the stored cap. Shape limits (1 to 5 characters, values in range, `unitsLost` only with a result) SHALL be validation errors. The server SHALL NOT compare a lineup unit's progression with the synced roster.

#### Scenario: The cap comes from the zone and slot

- **GIVEN** a current war in `guildWarSeasonA` at battlefield level 5
- **WHEN** the caller logs an attack on `Armoury` slot 2
- **THEN** the stored attack has `rarityCap` `Epic`

#### Scenario: A unit attacks once per war

- **GIVEN** an attack with character `a` in the current war
- **WHEN** the caller logs another attack with `a`
- **THEN** the API responds `400` `guildWarUnitDeployed`, and the war is unchanged

#### Scenario: Tokens run out

- **GIVEN** a current war in a season with `maxAttempts` 10 and 10 attacks
- **WHEN** the caller logs another attack
- **THEN** the API responds `400` `guildWarTokensSpent`

### Requirement: Wars live and die with the profile

A profile's wars and attacks SHALL be deleted when the profile is purged and SHALL be carried by the staging data transfer with new ids, keeping every field, their order and which war is current.

#### Scenario: Purge

- **WHEN** a profile with two wars is purged
- **THEN** no war or attack of that profile remains, and another profile's wars are untouched
