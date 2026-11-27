## Context

`TacticusPlanner.GameCatalog` embeds the raw catalog (`Data/**`, 24 MB, most of it the pinned datamine copies) and `GameCatalogLoader.Load(asOf)` turns it into a `GameCatalogSnapshot` on every process start: datamine readers, denormalizers, validators, a canonical-JSON hash per served payload, the contract (`schemaHash`) hash and the release digest. `EmbeddedGameCatalogProvider` runs it again in full when the UTC day changes, because the `events-calendar` projection starts at the calendar day.

`GameCatalogSnapshot` mixes two things: the served views (what clients download and most API features read) and the raw collections the validators need. A few API features also read raw collections directly.

Measured at `62ddbce`, one core, production GC settings (bench: a console app calling `GameCatalogLoader.Load`, pinned with `taskset -c 0`):

| Step | Time | Allocated |
| --- | --- | --- |
| First load (cold JIT) | 3.7 to 4.2 s | 403 MB |
| Day-change reload | 1.6 to 2.8 s | 298 MB |
| of which: validation | 26 ms | 7 MB |
| of which: hashing the served payloads | 0.9 s | 167 MB |
| Prototype: deserialize precompiled served views and runtime raw collections | 0.85 to 0.98 s | 57 MB (36 MB live, against 49 MB) |

## Goals / Non-Goals

**Goals:**

- No denormalization, validation or payload hashing at runtime.
- The day change costs a calendar projection, not a catalog rebuild.
- Served bytes, dataset hashes, `schemaHash`, `version` and `sourceHash` identical to today for the same inputs and day.
- Bad catalog data still fails before anything is deployed: the build fails.

**Non-Goals:**

- Changing any served shape, endpoint or the manifest.
- Moving the calendar to build time (it depends on the day).
- Source-generated JSON metadata or lazy per-dataset loading. Both could cut the remaining ~0.9 s further and stay possible later.
- Serving two releases (`catalog-version-window`).

## Decisions

### Three projects

- **`TacticusPlanner.GameCatalog.Models`**: every record type under `Models/` (raw and served), `GameCatalogDatasets`, and the hashing helpers under `Utils/`. Both other projects reference it.
- **`TacticusPlanner.GameCatalog.Compiler`**: `Data/**`, `DatamineFiles`, the datamine readers, `Denormalization/**`, `Validation/**` and the full loader. It is an executable: `Compiler <outputDir>` compiles and writes the compiled catalog, or exits non-zero with the validation errors.
- **`TacticusPlanner.GameCatalog`** (runtime): the provider, the runtime loader, the lookups, the calendar projection and the lean `GameCatalogSnapshot`. It references the compiler with `ReferenceOutputAssembly="false"`, so the compiler is built first but never shipped.

Namespaces do not change (`TacticusPlanner.GameCatalog.Models`, `.Utils`, `.Validation`, ...), so API code keeps its `using`s.

The runtime cannot reference the compiler for its types without a cycle (the compiler would need the runtime's models, and the runtime needs the compiler's output), which is why the models get their own assembly.

*Alternative considered:* keep one project and run the compiler from a separate tool that references it, embedding the output in the API. That keeps the raw 24 MB in the shipped assembly and puts catalog resources in the API project. *Alternative considered:* commit the compiled JSON and check it in CI. That adds about 13 MB of generated JSON to every data PR's diff.

### Compiled output

Written by the compiler to `obj/<config>/game-catalog/` of the runtime project and embedded under `compiled/`:

- `compiled/manifest.json`: `version`, `schemaHash`, `gameVersion` and the hash of every served dataset except `events-calendar`.
- `compiled/datasets/{key}.json`: each served payload, serialized with the catalog's JSON options. Its hash in `manifest.json` is computed over these same objects.
- `compiled/runtime.json`: the calendar inputs (`event-definitions`, `event-occurrences`) and the raw collections API features still read (see below).

`schemaHash` covers every served payload type, including the calendar's, and is computed by the compiler. It depends only on types, so it cannot drift at runtime.

### Runtime load and day change

`GameCatalogLoader.Load(asOf)` (runtime, same name and signature) deserializes the compiled files once per process into a `CompiledGameCatalog`, then builds the day's snapshot: project `events-calendar` for `asOf`'s UTC day, hash it, compute `sourceHash` from the compiled hashes plus the calendar hash, and construct `GameCatalogSnapshot`. `EmbeddedGameCatalogProvider` keeps the compiled catalog and, on a day change, builds a new snapshot from it. The served view objects are shared between days. Response bodies are cached per snapshot (`ServedDatasets`), so they are serialized again once per day, as today.

### Lean runtime snapshot

`GameCatalogSnapshot` keeps: release metadata, `DatasetHashes`, `Manifest`, every served view, `EventsCalendar`, `EventDefinitions` and `EventOccurrences`, and a named list of raw collections that API features need and no served view covers. Rule for each runtime consumer of a raw collection: if a served view carries the same fields, the consumer moves to the view; otherwise the raw collection stays in the lean snapshot and in `runtime.json`. The full result is renamed `GameCatalogCompilation`; validators, denormalizers and `TacticusPlanner.GameCatalog.Tests` use it.

### Build integration

A target in the runtime project, before `CoreCompile`, gets the compiler's path with `GetTargetPath`, runs it with `$(DOTNET_HOST_PATH)` and adds the outputs as `EmbeddedResource`. Its only input is the compiler assembly (the raw data is embedded in it, so any data change changes it) and its output is `manifest.json`, so an unchanged catalog does not recompile. The Dockerfile copies and restores the new projects in its build stage; the final image gets only the publish output, which has no compiler or raw data.

### Proving the compiled catalog is faithful

- The compiler validates before it writes, so invalid data fails `dotnet build` locally and in CI.
- A `TacticusPlanner.GameCatalog.Tests` test compiles in process, loads the runtime snapshot for the same day and checks, for every served dataset, that the runtime payload serializes to exactly the compiler's bytes, and that `version`, `schemaHash` and `sourceHash` match.
- The manifest Verify snapshot (`GameCatalogSnapshotTests`) is unchanged.

## Raw datasets, served projection, manifest

No raw dataset, denormalization step or served projection changes. The public manifest and its snapshot test do not move.

## Migration

No EF Core migration and no backfill.

## Risks / Trade-offs

- **A compiled type that does not round-trip through JSON** would serve different bytes. The prototype round-tripped every served view and runtime raw collection byte for byte; the faithfulness test keeps it that way.
- **Build time:** about 4 s on a build whose catalog changed. Builds that do not touch the catalog skip the compile.
- **Stack review size:** the project split is mostly file moves. It lands as its own PR with no behavior change, so review can focus on the PRs that change behavior.

## PR plan (stacked, API repo only)

1. Lean runtime snapshot: rename the full result to `GameCatalogCompilation`, add the lean `GameCatalogSnapshot` mapped from it, move runtime consumers off raw collections. Behavior unchanged. Carries this OpenSpec change.
2. Split into `Models`, `Compiler` and runtime projects. Pure move, runtime still references the compiler normally.
3. Compile at build time: compiler executable and output, embed, runtime loader and provider, faithfulness test, Dockerfile, skill and docs. Measure.
