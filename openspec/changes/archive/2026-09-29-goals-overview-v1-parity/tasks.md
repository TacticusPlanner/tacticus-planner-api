## 1. Raw data and registry

- [x] 1.1 Copy V1's `characters-lvl-up-abilities.json` into `src/TacticusPlanner.GameCatalog/Data/character-ability-costs.json` unchanged, and verify a test compares row count (59) and a few sampled rows with V1
- [x] 1.2 Register `CharacterAbilityCosts` as a raw `Required` key and `CharacterAbilityCostsServed` as a served key in `GameCatalogDatasets`, and verify the loader finds the embedded file

## 2. Models and denormalization

- [x] 2.1 Add the raw record and the served `GameCatalogCharacterAbilityCostView` (`Level`, `Gold`, `Badges` as `GameCatalogAmountByRarity`), and thread them through `GameCatalogSnapshot` and `GameCatalogLoader`
- [x] 2.2 Implement the denormalizer (`level = lvl + 1`, rarity from V1's bands) and verify unit tests for the first/last record and every band boundary (8/9, 17/18, 26/27, 35/36, 50/51)
- [x] 2.3 Add validation (non-empty, consecutive `lvl` from 1, gold >= 0, badges > 0) and verify tests for a gap, an empty source, and a valid ladder

## 3. Endpoint and manifest

- [x] 3.1 Add `GetGameCatalogCharacterAbilityCostsEndpoint` at `game-catalog/character-ability-costs` (anonymous, same envelope), and verify an endpoint test returns levels 2 to 60 ascending
- [x] 3.2 Update the manifest hash list and its snapshot test, and verify the manifest includes `character-ability-costs`
- [x] 3.3 Rebuild to regenerate the OpenAPI artifact under `artifacts/openapi` and verify the diff contains only the new endpoint and schema

## 4. Verification

- [x] 4.1 Run the API test suite and build and verify they pass
- [ ] 4.2 Verify with the local stack that the endpoint serves the ladder and the apps client can fetch and parse it once the companion apps change lands
