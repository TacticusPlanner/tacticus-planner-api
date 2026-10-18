## 1. Model, data, validation

- [ ] 1.1 Add optional `Synergies` to `GameCatalogHordeModeCharacter`, served in authoring order, omitted when empty
- [ ] 1.2 Add the seed synergies from `design.md` to `Data/horde-mode-tiers.json`
- [ ] 1.3 Validate synergies (catalog character, not self, no duplicates, at most 6)
- [ ] 1.4 Bump `GameCatalogRelease.SchemaVersion` and `Version`

## 2. Tests and docs

- [ ] 2.1 `HordeModeTiersTests`: seed synergies and each validation scenario
- [ ] 2.2 Re-verify the manifest snapshot; regenerate the OpenAPI artifact
- [ ] 2.3 Document `synergies` in the `game-catalog-data` skill
- [ ] 2.4 Run the repo's build, format check and tests
