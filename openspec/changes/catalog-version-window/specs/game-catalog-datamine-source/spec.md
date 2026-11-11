# Spec Delta

## ADDED Requirements

### Requirement: The catalog pins a latest and a previous datamine commit

The repository SHALL contain two release folders, `src/TacticusPlanner.GameCatalog/Data/datamine/latest/` and `.../previous/`, each with a `REF` of exactly one line, LF-terminated, in one of two forms:

```
<tag> <sha>          // tag: "v" + datamine version (e.g. v1.43.157.2989); sha: 40 lowercase hex chars
local <path>         // copies taken from a data-mines working tree; development only
```

The SHA is authoritative. The tag is a label recorded when the pin was made, and SHALL NOT be re-resolved by the build. The `latest` release's `raw/VERSION` SHALL be a strictly newer game version than the `previous` release's.

#### Scenario: A release pin

- **WHEN** `latest/REF` is `v1.44.87.3088 7dfb0f1d0a2f9a5a0bc8b9eade7dcf29d872412c` and `previous/REF` is `v1.43.157.2989 b69413043ce26a645c94713604197a30aed78cf5`
- **THEN** `DatamineSourceTests` accepts both pins

#### Scenario: A local pin fails the tests

- **WHEN** either `REF` starts with `local`
- **THEN** `DatamineSourceTests` fails with a message saying that a `local` datamine pin cannot be merged, naming the release folder and `scripts/pull-datamine.cs` as the fix

#### Scenario: A malformed pin fails the tests

- **WHEN** either `REF` has a short SHA, more than one line, or a tag without the `v` prefix
- **THEN** `DatamineSourceTests` fails and names that `REF`

#### Scenario: Releases out of order fail the tests

- **WHEN** `previous/raw/VERSION` is `1.44.87.3088` and `latest/raw/VERSION` is `1.43.157.2989`, or both are equal
- **THEN** `DatamineSourceTests` fails and names both versions

## MODIFIED Requirements

### Requirement: Only allowlisted files are copied, unchanged

`Data/datamine/files.txt` SHALL list `raw/`-relative paths, one per line, sorted ordinally, with no globs, shared by both releases. Blank lines and text after `#` are ignored. For every release folder and every listed path, `<release>/raw/<path>` SHALL exist and be byte-identical to `raw/<path>` at that release's pinned commit. No other file SHALL exist under `<release>/raw/`. Each release's `files.sha256` SHALL hold one `<sha256 hex>  <path>` line per listed path, in the same order, recorded by the copy script.

#### Scenario: A hand-edited copy fails the tests

- **WHEN** a byte of `Data/datamine/previous/raw/gameconfig/items.json` changes and `previous/files.sha256` does not
- **THEN** `DatamineSourceTests` fails and names `previous` and `gameconfig/items.json`

#### Scenario: A missing or extra copy fails the tests

- **WHEN** a listed path has no copy in a release folder, or a file under a release's `raw/` is not listed
- **THEN** `DatamineSourceTests` fails and names the release and each such path

### Requirement: `scripts/pull-datamine.cs` refreshes the pin and copies

`dotnet run scripts/pull-datamine.cs -- <tag-or-sha>` SHALL roll the window:

- it moves the `latest` folder (copies, `files.sha256`, `REF`) to `previous`, replacing the old `previous`;
- it then fills `latest` from the resolved commit of `TacticusPlanner/tacticus-planner-data-mines`, using the caller's git credentials;
- it copies the allowlisted paths byte for byte, deletes unlisted copies, rewrites `files.sha256` and writes `REF` (a SHA argument records its release tag, or `untagged`).

`--refresh <release> <tag-or-sha>` SHALL re-pull one release in place without rolling. This is for a forced re-run that moved a tag, and for allowlist changes, which re-run `--refresh` for both releases.

Every mode SHALL print the upstream `raw/VERSION` and each path's status (added, changed, removed, unchanged).

#### Scenario: Local working tree

- **WHEN** a maintainer runs the script with `--refresh latest --source ../tacticus-planner-data-mines`
- **THEN** it copies from that working tree into `latest` and writes `latest/REF` as `local ../tacticus-planner-data-mines`

#### Scenario: Bump to a new release

- **WHEN** `latest` is `v1.44.87.3088`, `previous` is `v1.43.157.2989`, and a maintainer runs `dotnet run scripts/pull-datamine.cs -- v1.45.0.3200`
- **THEN** `previous` holds the former `latest` copies and `REF` (`v1.44.87.3088`), `latest/REF` holds `v1.45.0.3200` and its SHA, and `DatamineSourceTests` passes

#### Scenario: Re-extracted release moves its tag

- **WHEN** data-mines force-moved `v1.44.87.3088` to a new commit and a maintainer runs `--refresh latest v1.44.87.3088`
- **THEN** only `latest` changes, its `REF` records the new SHA, and `previous` is untouched

#### Scenario: A path leaves the allowlist

- **WHEN** a path is removed from `files.txt` and the script is re-run with `--refresh` for each release at the same pins
- **THEN** its copies and checksum lines are deleted from both releases, it is reported removed, and both `REF`s are unchanged

### Requirement: `pull-datamine` changes nothing when a listed file is missing upstream

The script SHALL exit non-zero, without writing any file in either release folder, when a listed path does not exist at the target commit or in the `--source` tree.

#### Scenario: Upstream removed a listed file

- **WHEN** a listed path does not exist at the commit being pulled into `latest`
- **THEN** the script exits non-zero, names the path, and leaves both release folders, including `previous`, exactly as they were

### Requirement: Denormalizers open datamine copies by raw path only

Files under `Data/datamine/<release>/raw/` SHALL be embedded with the logical name `datamine/<release>/<raw path>` (forward slashes, `<release>` being `latest` or `previous`). A snapshot SHALL open copies through a reader bound to one release: `Open(rawPath)` returns a read stream for an allowlisted raw path of that release, and throws `InvalidOperationException` naming the path for any other. The leaf-name lookup used for catalog keys SHALL never return a `datamine/` resource, so a datamine leaf name (`units.json`, `factions.json`, `items.json`) can never satisfy or collide with a catalog key.

#### Scenario: Open by raw path

- **WHEN** the `previous` snapshot's denormalizer opens `gameconfig/items.json`
- **THEN** it reads the `previous` release's copy of `raw/gameconfig/items.json`, never `latest`'s

#### Scenario: Unlisted path

- **WHEN** code opens `gameconfig/units/lineup.json` while that path is not allowlisted
- **THEN** it throws, naming the path and `files.txt`

#### Scenario: No leaf collision

- **WHEN** the catalog loads its raw keys by leaf name with both releases' datamine copies embedded
- **THEN** no key resolves to a `datamine/` resource and the load succeeds as before

### Requirement: The catalog game version is the pinned datamine's build

Each served release's `GameVersion` SHALL be the trimmed contents of that release's `raw/VERSION` (the game build the datamine was extracted from). Its `Version` SHALL be `<GameVersion>+<12 hex>`, the hash being over that release's datamine copies and the shared curated data. Loading SHALL fail when either `raw/VERSION` is empty.

#### Scenario: Game version follows the pin

- **GIVEN** `latest/raw/VERSION` holds `1.44.87.3088` and `previous/raw/VERSION` holds `1.43.157.2989`
- **WHEN** the manifest is requested without and with `gameVersion=1.43.157.2989`
- **THEN** their `GameVersion` values are `1.44.87.3088` and `1.43.157.2989`

#### Scenario: A curated change moves both versions

- **WHEN** a curated file changes and neither datamine pin does
- **THEN** both releases' `Version` values change and their `GameVersion` values do not

## REMOVED Requirements

### Requirement: The catalog pins one datamine commit in `REF`

**Reason**: The catalog now serves two releases, each with its own pin; see "The catalog pins a latest and a previous datamine commit".

**Migration**: Move `Data/datamine/{raw,REF,files.sha256}` into `Data/datamine/latest/`, and pull the prior release into `Data/datamine/previous/` with `pull-datamine --refresh previous <tag>`.
