## Why

The Guild War maps the planner shows are the zoomed-out board pictures, with no playable area, hex grid or deployment hexes, so players can't match them to the game (Guild War feedback, point 5). The game ships each board's battle grid in its board bundles; data-mines now extracts it to `raw/boards/<boardId>.json` (data-mines #11, release `v1.44.126.3127`).

Companion change: `guild-war-board-grids` in `tacticus-planner-apps` (draws the cropped picture with the hex overlay, deployment zones and blocked hexes). This API half applies first.

## What Changes

- Each `guild-war` map serves its `board`: the logical grid size, where it sits on the visual board, every tile's terrain, elevation and blocked flag, and the attacker and defender spawn cells of the season's spawn point set.
- The 17 live map-pool boards join the datamine allowlist.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guild-war-dataset`: maps carry their board grid.

## Impact

- `TacticusPlanner.GameCatalog`: `boards/<boardId>.json` for the map pool joins `Data/datamine/files.txt`; new `DatamineBoard` shapes and served `GameCatalogGuildWarBoard` / `Tile` / `Cell`; `GuildWarDenormalizer` reads and validates the boards. The `guild-war` hash and the derived `schemaHash` move; the OpenAPI artifact gains the board types. The served dataset grows to about 370 KB (27 KB gzipped): boards repeat on the four seasons that share them.
- Additive for clients: existing `boardId` / `spawnPointsSet` are unchanged.
