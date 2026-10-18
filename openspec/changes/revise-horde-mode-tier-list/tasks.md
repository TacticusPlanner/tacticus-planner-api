## 1. Data and validation

- [ ] 1.1 Replace `Data/horde-mode-tiers.json` with tiers S 1.90, A 1.70, B 1.45, C 1.20, D 1.00 and the 47 characters in `design.md`, each with its own `multiplier`
- [ ] 1.2 Add `"D"` to `HordeModeTierNames` in `Validation/HordeModeTiersValidation.cs` and update its doc comment
- [ ] 1.3 Bump `GameCatalogRelease.Version`

## 2. Tests

- [ ] 2.1 Update `HordeModeTiersTests`: seed tier names and multipliers, seed entries from the spec scenario, 47 characters; move the unknown-tier case to `E`; add "tier D accepted" and "missing tier D" cases
- [ ] 2.2 Re-verify the manifest snapshot (`GameCatalogManifestMatchesSnapshot`)
- [ ] 2.3 Run the repo's build, format check and tests

## 3. Docs

- [ ] 3.1 Update the `horde-mode-tiers` notes in the `game-catalog-data` skill if they name the four tiers
