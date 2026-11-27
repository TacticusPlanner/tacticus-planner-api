## ADDED Requirements

### Requirement: The catalog is compiled at build time

The game catalog's datamine readers, denormalizers, validators, served-payload hashes, `schemaHash` and release metadata SHALL run at build time, in `TacticusPlanner.GameCatalog.Compiler`, and SHALL NOT run in the API process. Where a dataset spec says invalid data fails the build or fails startup, the catalog compilation in the build SHALL fail.

#### Scenario: Invalid data fails the build

- **WHEN** a raw file holds data that fails catalog validation (for example an occurrence whose `startUtc` is after its `endUtc`)
- **THEN** `dotnet build` fails with the validation error and no compiled catalog is written

### Requirement: The runtime embeds the compiled catalog, not the raw data

The compiler SHALL write every served dataset except `events-calendar` as JSON serialized with the catalog's JSON options, a manifest file with `version`, `schemaHash`, `gameVersion` and each of those datasets' hash, and a runtime file with `event-definitions`, `event-occurrences` and the raw collections the API reads at runtime. The runtime project SHALL embed these files and SHALL NOT embed the raw `Data/**` files.

#### Scenario: The shipped API carries no raw catalog

- **WHEN** the API is published
- **THEN** the publish output contains neither the compiler assembly nor any `Data/datamine` file, and the runtime assembly's catalog resources are the compiled files only

#### Scenario: An unchanged catalog is not recompiled

- **WHEN** the API is built again and the compiler assembly has not changed
- **THEN** the compiler does not run and the previous compiled catalog is embedded

### Requirement: The runtime serves exactly what the compiler produced

The runtime SHALL load the compiled catalog without re-running denormalization, validation or payload hashing. Every dataset the compiler wrote SHALL serialize to the same bytes the compiler wrote. For a given UTC day, every served dataset's payload, `events-calendar` included, SHALL serialize to the same bytes a full in-process compilation of the same raw data produces for that day, and `version`, `schemaHash`, `gameVersion`, every dataset hash and `sourceHash` SHALL equal what a full in-process compilation of the same raw data computes for that day.

#### Scenario: Compiled and runtime catalogs agree

- **WHEN** a test compiles the catalog in process and loads the runtime snapshot for the same UTC day
- **THEN** every served dataset serializes identically in both, and both manifests are equal

#### Scenario: The public manifest does not move

- **WHEN** the manifest endpoint is requested after this change, for unchanged raw data
- **THEN** it equals the manifest served before the change, except the time-dependent `events-calendar` hash and `sourceHash`

### Requirement: The runtime projects only the events calendar

The runtime SHALL project `events-calendar` from the compiled `event-definitions` and `event-occurrences` for the current UTC day, hash it, and compute `sourceHash` from the compiled release metadata and dataset hashes plus the calendar hash. When the UTC day changes, the runtime SHALL build the next snapshot by re-projecting the calendar only; every other served dataset SHALL carry over unchanged.

#### Scenario: The day changes

- **WHEN** the first request after 00:00 UTC reads the catalog
- **THEN** the snapshot it gets has the new day's `events-calendar`, `events-calendar` hash and `sourceHash`, and every other dataset hash is unchanged

### Requirement: API features read the lean runtime snapshot

`GameCatalogSnapshot` SHALL hold the release metadata, the dataset hashes and manifest, every served view, `events-calendar`, `event-definitions`, `event-occurrences`, and only those raw collections that an API feature reads and no served view covers. A runtime consumer SHALL read a served view when one carries the fields it needs. The full compile-time result, with every raw collection the validators use, SHALL be `GameCatalogCompilation` and SHALL exist only in the compiler and its tests.

#### Scenario: A consumer needs a field only raw data has

- **WHEN** an API feature needs a raw field no served view carries
- **THEN** that raw collection is part of the compiled runtime file and of `GameCatalogSnapshot`, listed by name
