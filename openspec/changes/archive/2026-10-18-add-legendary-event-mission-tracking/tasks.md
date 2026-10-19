## 1. Catalog

- [x] 1.1 Mission record model and template parser in `LreDenormalizer` with the faction/trait/damage-type alias table
- [x] 1.2 Loader rejects unmatched lines and unknown aliases; tests over all raw `lres-*.json` files and a failing fixture
- [x] 1.3 Bump `SchemaVersion` and `Version`; update the `game-catalog-data` skill

## 2. Plan run inputs

- [x] 2.1 Contracts, mutation and validation for the completed arrays; projection serves them sorted
- [x] 2.2 EF configuration and migration
- [x] 2.3 Endpoint tests for the scenarios

## 3. Verification

- [x] 3.1 `dotnet build` and `dotnet test`
