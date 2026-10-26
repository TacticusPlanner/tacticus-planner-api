# Tasks

Companion apps change: none (shapes unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 6.

## 1. Binding

- [x] 1.1 Add the eight LRE datamine files to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineLres` and the datamine and curation models
- [x] 1.3 Move display names, run starts, battle points, filters and restriction names to `Data/curated/lre-curation.json`
- [x] 1.4 Delete `Data/lres/*.json` and `GameCatalogDatasets.LreEvents`; load `groups.json` once

## 2. Verification

- [x] 2.1 Compare old and new: differences limited to battle powers, The Lost and the Damned, chest ladders and two premium mission lists
- [x] 2.2 Add `LreDatamineReaderTests`; update the Uthar mission, chest ladder and schedule tests
- [x] 2.3 Promote the manifest snapshot (`lres`, `lre-battles`)
- [x] 2.4 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both copies)
