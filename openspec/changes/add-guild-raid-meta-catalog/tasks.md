## 1. Catalog dataset and validation

- [x] 1.1 Add the embedded `guild-raid-meta` source fixture with its source/update metadata, ordered Comp profiles, and published boss Meta/alternate recommendations, and verify it contains only stable ids and passes catalog source parsing.
- [x] 1.2 Add raw and served Guild Raid Meta models plus denormalization that preserves authored ordering, and verify focused `TacticusPlanner.GameCatalog.Tests` coverage asserts the served projection.
- [x] 1.3 Validate recommendation cardinality/uniqueness and every boss, character, Machine of War, signature, and Comp reference while loading the catalog, and verify malformed fixture coverage fails before the dataset can be served.

## 2. Manifest and anonymous contract

- [x] 2.1 Register `guild-raid-meta` in the catalog snapshot, manifest, content/source hashing, and anonymous dataset endpoint, and verify the dataset is independently discoverable and downloadable from `game-catalog/guild-raid-meta`.
- [x] 2.2 Update the catalog manifest snapshot and endpoint/OpenAPI contract artifacts, and verify an editorial-only fixture update changes only the Guild Raid Meta manifest hash while the `raid-bosses` hash stays stable.
- [x] 2.3 Coordinate the published payload and generated contract with the companion `tacticus-planner-apps` change `add-guild-raid-meta-catalog`, and verify the client-facing field names and id-only boundary match its schema before the apps half is applied.

## 3. Repository verification

- [x] 3.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` and verify it exits successfully.
- [x] 3.2 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore` and verify the Release build succeeds.
- [x] 3.3 Run `dotnet test TacticusPlanner.slnx -c Release --no-build` and verify the full test suite, including catalog validation and manifest coverage, passes.
