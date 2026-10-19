## 1. Model, data, validation

- [x] 1.1 Add optional `Synergies` to `GameCatalogHordeModeCharacter`, served in authoring order, omitted when empty
- [x] 1.2 Add the seed synergies from `design.md` to `Data/horde-mode-tiers.json`
- [x] 1.3 Validate synergies (catalog character, not self, no duplicates, at most 6)
- [x] 1.4 Bump `GameCatalogRelease.SchemaVersion` and `Version`

## 2. Tests and docs

- [x] 2.1 `HordeModeTiersTests`: seed synergies and each validation scenario
- [x] 2.2 Re-verify the manifest snapshot; regenerate the OpenAPI artifact
- [x] 2.3 Document `synergies` in the `game-catalog-data` skill
- [x] 2.4 Run the repo's build, format check and tests (the .NET SDK cannot be downloaded in this environment; CI runs them; the manifest hash was recomputed with a script that reproduces the previous hash)
