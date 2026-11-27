## 1. Lean runtime snapshot (PR 1)

- [x] 1.1 Rename the full load result to `GameCatalogCompilation` (validators, denormalizers, GameCatalog tests)
- [x] 1.2 Add the lean `GameCatalogSnapshot` (release metadata, hashes, manifest, served views, calendar inputs, named raw collections) built from the compilation
- [x] 1.3 Move runtime consumers of raw collections to served views where a view carries the fields; keep the rest as named raw collections
- [x] 1.4 Targeted tests, `dotnet format`, manifest snapshot unchanged

## 2. Project split (PR 2)

- [x] 2.1 Create `TacticusPlanner.GameCatalog.Models` and move `Models/**`, `Utils/**`
- [x] 2.2 Create `TacticusPlanner.GameCatalog.Compiler` and move `Data/**`, `DatamineFiles`, `Denormalization/**`, `Validation/**`, the full loader
- [x] 2.3 Runtime references both normally; solution, lock files, Dockerfile restore/copy steps, test project references
- [x] 2.4 Build, targeted tests, `dotnet format`; no behavior change

## 3. Compile at build time (PR 3)

- [ ] 3.1 Compiler entry point writes `manifest.json`, `datasets/{key}.json` and `runtime.json`, exits non-zero on validation errors
- [ ] 3.2 Runtime build target runs the compiler (inputs: compiler assembly) and embeds the outputs; compiler reference without its output assembly
- [ ] 3.3 Runtime loader deserializes the compiled catalog and projects the calendar; provider re-projects only the calendar on a day change
- [ ] 3.4 Faithfulness test: in-process compilation and runtime snapshot serve identical bytes and manifests
- [ ] 3.5 Dockerfile builds and the published image has no compiler or raw data
- [ ] 3.6 Measure cold load and day change on one core with production GC settings; record in the PR
- [ ] 3.7 Update the `game-catalog-data` skill and `AGENTS.md`
