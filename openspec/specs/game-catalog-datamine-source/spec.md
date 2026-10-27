# game-catalog-datamine-source Specification

## Purpose

Lets the game catalog read raw game config from a pinned `tacticus-planner-data-mines` commit: the pin, the allowlist of copied files, the copy script's contract, the checks that keep the copies honest, and how denormalizers open them.

## Requirements

### Requirement: The catalog pins one datamine commit in `REF`

The repository SHALL contain `src/TacticusPlanner.GameCatalog/Data/datamine/REF` with exactly one line, LF-terminated, in one of two forms:

```
<tag> <sha>          // tag: "v" + datamine version (e.g. v1.43.157.2989); sha: 40 lowercase hex chars
local <path>         // copies taken from a data-mines working tree; development only
```

The SHA is authoritative. The tag is a label recorded when the pin was made, and SHALL NOT be re-resolved by the build.

#### Scenario: A release pin

- **WHEN** `REF` is `v1.43.157.2989 e5c8c9bc749a7d1bbfb33a3d93056e8826e82678`
- **THEN** `DatamineSourceTests` accepts the pin

#### Scenario: A local pin fails the tests

- **WHEN** `REF` starts with `local`
- **THEN** `DatamineSourceTests` fails with a message saying that a `local` datamine pin cannot be merged, and naming `scripts/pull-datamine.cs` as the fix

#### Scenario: A malformed pin fails the tests

- **WHEN** `REF` has a short SHA, more than one line, or a tag without the `v` prefix
- **THEN** `DatamineSourceTests` fails and names `REF`

### Requirement: Only allowlisted files are copied, unchanged

`Data/datamine/files.txt` SHALL list `raw/`-relative paths, one per line, sorted ordinally, with no globs. Blank lines and text after `#` are ignored. For every listed path, `Data/datamine/raw/<path>` SHALL exist and be byte-identical to `raw/<path>` at the pinned commit. No other file SHALL exist under `Data/datamine/raw/`. `Data/datamine/files.sha256` SHALL hold one `<sha256 hex>  <path>` line per listed path, in the same order, recorded by the copy script.

#### Scenario: A hand-edited copy fails the tests

- **WHEN** a byte of `Data/datamine/raw/gameconfig/items.json` changes and `files.sha256` does not
- **THEN** `DatamineSourceTests` fails and names `gameconfig/items.json`

#### Scenario: A missing or extra copy fails the tests

- **WHEN** a listed path has no copy, or a file under `Data/datamine/raw/` is not listed
- **THEN** `DatamineSourceTests` fails and names each such path

### Requirement: `scripts/pull-datamine.cs` refreshes the pin and copies

`dotnet run scripts/pull-datamine.cs -- <tag-or-sha>` SHALL resolve the argument to a commit of `TacticusPlanner/tacticus-planner-data-mines` with the caller's git credentials, copy the allowlisted paths at that commit byte for byte, delete unlisted copies, rewrite `files.sha256`, and write `REF` (a SHA argument records its release tag, or `untagged`). It SHALL print the upstream `raw/VERSION` and each path's status (added, changed, removed, unchanged).

#### Scenario: Local working tree

- **WHEN** a maintainer runs the script with `--source ../tacticus-planner-data-mines`
- **THEN** it copies from that working tree and writes `REF` as `local ../tacticus-planner-data-mines`

#### Scenario: Bump to a new release

- **WHEN** a maintainer runs `dotnet run scripts/pull-datamine.cs -- v1.44.0.3000` and `items.json` changed upstream
- **THEN** `REF` holds `v1.44.0.3000` and that tag's SHA, the `items.json` copy and its checksum line are updated, unchanged copies are reported unchanged, and `DatamineSourceTests` passes

#### Scenario: A path leaves the allowlist

- **WHEN** a path is removed from `files.txt` and the script is re-run at the same pin
- **THEN** its copy and checksum line are deleted, it is reported removed, and `REF` is unchanged

### Requirement: `pull-datamine` changes nothing when a listed file is missing upstream

The script SHALL exit non-zero, without writing any file, when a listed path does not exist at the target commit or in the `--source` tree.

#### Scenario: Upstream removed a listed file

- **WHEN** a listed path does not exist at the target commit
- **THEN** the script exits non-zero, names the path, and leaves `REF`, the copies and `files.sha256` unchanged

### Requirement: Denormalizers open datamine copies by raw path only

Files under `Data/datamine/raw/` SHALL be embedded with the logical name `datamine/<raw path>` (forward slashes). `DatamineFiles.Open(rawPath)` SHALL return a read stream for an allowlisted raw path, and throw `InvalidOperationException` naming the path for any other. The leaf-name lookup used for catalog keys SHALL never return a `datamine/` resource, so a datamine leaf name (`units.json`, `factions.json`, `items.json`) can never satisfy or collide with a catalog key.

#### Scenario: Open by raw path

- **WHEN** a denormalizer calls `DatamineFiles.Open("gameconfig/items.json")`
- **THEN** it reads the pinned copy of `raw/gameconfig/items.json`

#### Scenario: Unlisted path

- **WHEN** code calls `DatamineFiles.Open("gameconfig/units/lineup.json")` while that path is not allowlisted
- **THEN** it throws, naming the path and `files.txt`

#### Scenario: No leaf collision

- **WHEN** the catalog loads its raw keys by leaf name with datamine copies embedded
- **THEN** no key resolves to a `datamine/` resource and the load succeeds as before

### Requirement: Adding the datamine source changes no served data

Applying this change SHALL NOT change any served dataset, its hash, the manifest shape, `SchemaVersion` or `GameVersion`. Moving hand-kept files to `Data/curated/` SHALL keep their leaf names and contents.

#### Scenario: Snapshot unchanged

- **WHEN** `GameCatalogSnapshotTests` runs after this change
- **THEN** the received manifest equals the current `*.verified.txt` with no edits
