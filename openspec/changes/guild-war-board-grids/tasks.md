## 1. Board grids

- [x] 1.1 Add the live map-pool boards (`boards/<boardId>.json`) to `Data/datamine/files.txt` and pull them from `v1.44.126.3127`, and verify `DatamineSourceTests` passes.
- [x] 1.2 Add the `DatamineBoard` shapes and the served `GameCatalogGuildWarBoard`, `GameCatalogGuildWarTile` and `GameCatalogGuildWarCell`; read and validate each map's board in `GuildWarDenormalizer` per the `guild-war-dataset` spec, and verify tests for `PVP_desert_10`'s grid and spawns, the 0-based `LHE_Desert_06` set and a forced-unplayable tile, and the load failures (spawn set out of range, wrong board id, missing attacker spawns).
- [x] 1.3 Promote the manifest snapshot (`guild-war` hash and `schemaHash` moved) and verify the regenerated `artifacts/openapi` has the board types; hand the contract to the `guild-war-board-grids` apps change.
