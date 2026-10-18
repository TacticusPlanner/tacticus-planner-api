## 1. Model and data

- [ ] 1.1 Add `GameCatalogHordeModeRelic(EquipmentId, Multiplier)` and an optional `Relics` list on `GameCatalogHordeModeCharacter`
- [ ] 1.2 Serve `relics` sorted by `equipmentId`, omitted when empty, in `HordeModeTiersDenormalizer`
- [ ] 1.3 Add the eight seed relic ratings from `design.md` to `Data/horde-mode-tiers.json`
- [ ] 1.4 Bump `GameCatalogRelease.SchemaVersion` and `Version`

## 2. Validation

- [ ] 2.1 Pass relic ids with their `allowedUnits` into `ValidateHordeModeTiers`; reject non-relics, disallowed characters, duplicates, out-of-bounds and below-base multipliers

## 3. Tests and docs

- [ ] 3.1 `HordeModeTiersTests`: seed relic, sorting/hash, each validation scenario
- [ ] 3.2 Re-verify the manifest snapshot; regenerate the OpenAPI artifact
- [ ] 3.3 Document `relics` in the `game-catalog-data` skill
- [ ] 3.4 Run the repo's build, format check and tests
