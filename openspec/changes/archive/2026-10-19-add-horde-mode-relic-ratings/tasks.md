## 1. Model and data

- [x] 1.1 Add `GameCatalogHordeModeRelic(EquipmentId, Multiplier)` and an optional `Relics` list on `GameCatalogHordeModeCharacter`
- [x] 1.2 Serve `relics` sorted by `equipmentId`, omitted when empty, in `HordeModeTiersDenormalizer`
- [x] 1.3 Add the eight seed relic ratings from `design.md` to `Data/horde-mode-tiers.json`
- [x] 1.4 Bump `GameCatalogRelease.SchemaVersion` and `Version`

## 2. Validation

- [x] 2.1 Pass relic ids with their `allowedUnits` into `ValidateHordeModeTiers`; reject non-relics, disallowed characters, duplicates, out-of-bounds and below-base multipliers

## 3. Tests and docs

- [x] 3.1 `HordeModeTiersTests`: seed relic, sorting/hash, each validation scenario
- [x] 3.2 Re-verify the manifest snapshot; regenerate the OpenAPI artifact
- [x] 3.3 Document `relics` in the `game-catalog-data` skill
- [x] 3.4 Run the repo's build, format check and tests (the .NET SDK cannot be downloaded in this environment; CI runs them; the manifest hash was recomputed with a script that reproduces the previous hash)
