## Why

Every API process builds the whole game catalog when it starts. `GameCatalogLoader.Load` reads 24 MB of embedded datamine and curated JSON, denormalizes it into the 35 served datasets, validates it and hashes every served payload. Measured on one core with the production GC settings (`DOTNET_PROCESSOR_COUNT=1`, 640 MiB heap limit, `GCConserveMemory=5`) at `62ddbce`:

- First load: 3.7 to 4.2 s, allocating 403 MB, keeping 49 MB live. The production container has 0.5 vCPU, so a cold start spends roughly twice that before it can answer.
- Of that, validation is 26 ms and payload hashing about 0.9 s (167 MB). Denormalization and JIT make up the rest. Only about 0.3 s is JSON parsing.
- The catalog is rebuilt in full when the UTC day changes, inside whichever live request comes first: 1.6 to 2.8 s and 298 MB, only to re-project the events calendar.

None of this work depends on anything a running process knows except the calendar day. It depends only on the embedded files, which are fixed at build time. Doing it at build time takes it off every cold start, every scale-out and the first request after midnight. A prototype that loads the served datasets from precompiled JSON instead took 0.85 to 0.98 s and allocated 57 MB (36 MB live).

No companion apps change: the served datasets, the manifest and every endpoint stay byte-for-byte the same.

## What Changes

- **The catalog is compiled at build time.** A new build-time tool, `TacticusPlanner.GameCatalog.Compiler`, holds the raw data, the datamine readers, the denormalizers and the validators. During the build it loads the raw data, validates it and writes the compiled catalog: every served dataset except `events-calendar` as JSON, their hashes, the schema hash and the release metadata, plus the few raw collections the API still reads at runtime. A catalog that fails validation fails the build, as the dataset specs already say.
- **The runtime loads only the compiled catalog.** `TacticusPlanner.GameCatalog` embeds the compiled output instead of the raw data and deserializes it at startup. It no longer runs denormalizers, validators or payload hashing. The raw datamine copies leave the API assembly and the container image.
- **Only the events calendar is projected at runtime.** It depends on the UTC day, so the runtime keeps `event-definitions` and `event-occurrences` and projects the calendar at startup and again when the day changes. The day change re-projects only the calendar and recomputes the `events-calendar` hash and `sourceHash`. Every other dataset object, and its cached response bodies, carries over.
- **API code reads a lean snapshot.** `GameCatalogSnapshot` keeps the release metadata, the served views, the calendar inputs and a short, named list of raw collections that API features still need. Runtime consumers that read raw collections a served view already covers move to that view. The full compile-time result becomes `GameCatalogCompilation`, used by the compiler and its tests.
- **Three projects instead of one.** `TacticusPlanner.GameCatalog.Models` (the record types and hashing helpers, shared), `TacticusPlanner.GameCatalog.Compiler` (build time only) and `TacticusPlanner.GameCatalog` (runtime). Namespaces stay as they are, so API code does not change its `using`s.

No **BREAKING** changes. No endpoint, OpenAPI, served-shape or manifest change. No database change.

## Capabilities

### New Capabilities

- `game-catalog-compilation`: where the catalog is built, what the compiled output holds, what the runtime still computes, and how the build, the runtime and the tests prove the compiled catalog matches the raw data.

### Modified Capabilities

- `game-events-calendar-dataset`: the day change re-projects the calendar only, instead of rebuilding the whole served catalog.

## Impact

- **`src/TacticusPlanner.GameCatalog`** splits into `TacticusPlanner.GameCatalog.Models`, `TacticusPlanner.GameCatalog.Compiler` and the runtime `TacticusPlanner.GameCatalog`. `Data/**`, `Denormalization/**`, `Validation/**`, `DatamineFiles` and the full loader move to the compiler. `EmbeddedGameCatalogProvider` and `GameCatalogLoader` (runtime) change.
- **API features reading raw collections:** campaign battle and campaign event progress overrides, the player-data transformer, team and Guild War catalog facts, guild raid status, the V1 importers and `GameCatalogLreLookups`.
- **Build:** `TacticusPlanner.slnx`, the Dockerfile's restore and copy steps, and lock files for the new projects. A build whose compiler inputs changed runs the compiler once (about 4 s).
- **Tests:** `TacticusPlanner.GameCatalog.Tests` references the compiler and adds a test that the runtime snapshot serves exactly what the compiler produced. The manifest snapshot test does not move.
- **Skill and docs:** the `game-catalog-data` skill and `AGENTS.md` describe the new layout.
- **Related:** the `catalog-version-window` proposal (two releases loaded side by side) would double today's startup cost. With a compiled catalog the compiler emits one compiled set per release.
