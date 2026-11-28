## MODIFIED Requirements

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
- `maps` (array, game order): each `{ boardId (string), spawnPointsSet (integer), board }`, where `board` is the battle grid defined by "Guild War maps carry their board grid"
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

## ADDED Requirements

### Requirement: Guild War maps carry their board grid

Each `maps` entry SHALL carry `board`, read from the datamine `boards/{boardId}.json` (the game's `BoardConfiguration` as `board` and `IdunBoardType` as `visual`, written by data-mines `extract.py --boards`). `boards/{boardId}.json` SHALL be on the datamine allowlist for every board of a live season's `mapPool`. `board` SHALL carry exactly:

- `columns` (integer), `rows` (integer): the logical grid, `board.Width` x `board.Height` pointy-top hexes, row 0 on the attacker's side
- `visualColumns` (integer), `visualRows` (integer): the visual board, `visual.Width` x `visual.Height`
- `visualFirstColumn` (integer), `visualFirstRow` (integer): where logical (0, 0) sits on the visual board, `visual.PlayableMinCol` and `visual.PlayableMinRow`; a row whose visual row index is odd is shifted half a hex to the right
- `tiles` (array, row 0 first, each row by column ascending): each `{ column (integer), row (integer), terrain (string), elevation (integer), blocked (boolean) }`, from `board.Tiles[column].Tile[row]`: `terrain` is `TileId` (Grass, Water, Bridge, DryBush, Swamp, Block today), `elevation` is `Elevation` (2 is ground), `blocked` is true when `TileId` is `Block` or `ForceUnplayable` is not 0
- `attackerSpawns`, `defenderSpawns` (arrays of `{ column (integer), row (integer) }`, game order): the spawn points of team 0 (the player's team) and team 1 in `board.SpawnPointSets[spawnPointsSet]`, a 0-based index; groups of other teams are not served

The load SHALL fail when a map-pool board file is missing, its `board.Id` differs from `boardId`, `Width` or `Height` is not positive, `Tiles` is not `Width` columns of `Height` tiles, a tile has no `TileId` or `Elevation`, the logical grid does not fit the visual board, `spawnPointsSet` is not an index of `SpawnPointSets`, a team 0 or 1 spawn point is off the grid, or the set has no team 0 or no team 1 spawn point.

#### Scenario: A board's grid and the season's spawn set

- **GIVEN** `guildWarSeasonA`'s map pool lists `PVP_desert_10` with `spawnPointsSet` 1
- **WHEN** a client reads that map's `board`
- **THEN** it is 7 x 10 on a 13 x 15 visual board starting at (3, 2), with 70 tiles whose first is `{ column 0, row 0, terrain Block, elevation 2, blocked true }`, attacker spawns (0, 2), (1, 3), (3, 1), (5, 0), (6, 0) and 20 defender spawns

#### Scenario: The spawn point set index is 0-based

- **GIVEN** the map pool lists `LHE_Desert_06` with `spawnPointsSet` 0
- **WHEN** a client reads that map's `board`
- **THEN** its spawns are those of the board's first spawn point set
