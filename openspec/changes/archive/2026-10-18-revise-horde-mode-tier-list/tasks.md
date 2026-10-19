## 1. Data and validation

- [x] 1.1 Replace `Data/horde-mode-tiers.json` with tiers S 1.90, A 1.70, B 1.45, C 1.20, D 1.00 and the 47 characters in `design.md`, each with its own `multiplier`
- [x] 1.2 Add `"D"` to `HordeModeTierNames` in `Validation/HordeModeTiersValidation.cs` and update its doc comment
- [x] 1.3 `GameCatalogRelease.Version` is already `dev-2026-10-18` (date-based), so no bump

## 2. Tests

- [x] 2.1 Update `HordeModeTiersTests`: seed tier names and multipliers, seed entries from the spec scenario, 47 characters; move the unknown-tier case to `E`; add "tier D accepted" and "missing tier D" cases
- [x] 2.2 Re-verify the manifest snapshot (`GameCatalogManifestMatchesSnapshot`)
- [x] 2.3 Build, format check and tests run in CI (no .NET SDK reachable in the authoring environment); the manifest hash was recomputed with the canonical-JSON algorithm and matches the old value on the old file

## 3. Docs

- [x] 3.1 Update the `horde-mode-tiers` notes in the `game-catalog-data` skill if they name the four tiers
