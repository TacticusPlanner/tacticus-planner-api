# Proposal

## Why

The catalog's raw `Data/**` is hand-kept: each game patch is reconciled by hand against the V1 frontend's datamine, which has regressed in places (`game-catalog-data` skill, "Updating to a new in-game version"), and there is no re-runnable pipeline. `TacticusPlanner/tacticus-planner-data-mines` now extracts the game's own config from every PC build (`raw/`, latest version only, one release tag `v<version>` per merged datamine). Its `docs/catalog-audit.md` maps almost every catalog dataset to a datamine source, and its `docs/roadmap.md` step 4 asks for this change: a way for the API to pin a datamine commit and copy the raw files it needs, so the later per-dataset migrations (roadmap steps 5 to 7) each become "bind a denormalizer to a pinned raw file" instead of "reconcile JSON by hand".

This change builds that plumbing only. No served dataset changes source here; the pilot (`items` → `equipment`, roadmap step 5) is the next change.

## What Changes

- **Pin.** New `Data/datamine/REF`: the data-mines release tag and the full commit SHA it points at. The SHA is authoritative (a forced re-run moves the tag). A `local` pin marks files copied from a working tree during development.
- **Allowlist.** New `Data/datamine/files.txt`: the `raw/`-relative paths the API copies, one per line. A file is listed only when a denormalizer reads it (or will in the change that lists it). This change seeds `gameconfig/items.json` and `gameconfig/globalValues.json` for the pilot.
- **Copy.** New C# file-based app `scripts/pull-datamine.cs` (`dotnet run scripts/pull-datamine.cs -- <ref>` or `--source <path>`): reads the pinned commit (blob-filtered clone, only allowlisted blobs fetched, with the caller's git credentials) or reads a local data-mines working tree, copies every allowlisted file byte for byte to `Data/datamine/raw/<path>`, deletes copies no longer listed, and writes `REF` and `Data/datamine/files.sha256`. No renames or value changes on copy.
- **CI gate.** A `TacticusPlanner.GameCatalog.Tests` test (so `dotnet test` locally and in CI) rejects a `local` pin, a malformed `REF`, an allowlisted file that is missing, an unlisted file under `Data/datamine/raw/`, and any copy whose SHA-256 differs from `files.sha256` (a hand edit). Verifying the copies against the private upstream needs a cross-repo token and is deferred to roadmap step 9.
- **Loader.** Datamine copies are embedded under their own `datamine/` logical name and opened by full raw path (`DatamineFiles.Open("gameconfig/items.json")`), never by the leaf-name convention. The game's leaf names (`units.json`, `factions.json`, `upgrades.json`, `items.json`) would otherwise collide with catalog keys.
- **Curated data.** Hand-kept files with no game source move to `Data/curated/` (`event-definitions.json`, `event-occurrences.json`, `horde-mode-tiers.json`, `guild-raid-meta/*`, `enums.json`). This is a `git mv` only: leaf names, contents and hashes do not change.
- **Rules for later migrations** (in the skill doc, not code): each dataset migrates in its own change and either keeps its served shape (the manifest snapshot proves no hash moved) or renames to the game's terms (`SchemaVersion` bump plus the apps update in the same pair), never both. "Reconcile against V1" becomes "bump `REF`".
- **Visibility.** This repo is public and data-mines is private. The copies are committed unchanged, so allowlisted files become public, including unreleased units in raw files the catalog itself skips. Severyn accepted this (design D1).

No served dataset, manifest field or `SchemaVersion` changes. The manifest snapshot stays byte-identical.

## Capabilities

### New Capabilities

- `game-catalog-datamine-source`: the pin, the allowlist, the copy script's contract, the CI checks, and how the loader exposes datamine copies to denormalizers.

### Modified Capabilities

None. No served projection changes.

## Impact

- Companion apps change: **none**. No served shape changes, so `tacticus-planner-apps` needs nothing here. Apps work starts at the per-dataset migrations that rename (roadmap step 6) and the new datasets (step 7).
- `src/TacticusPlanner.GameCatalog`: `TacticusPlanner.GameCatalog.csproj` (separate embed for `Data/datamine/raw/**`), `GameCatalogLoader.OpenEmbeddedJson` (ignores `datamine/` resources), new `DatamineFiles.cs`, new `Data/datamine/{REF,files.txt,files.sha256,raw/**}`, `Data/curated/` moves.
- `scripts/pull-datamine.cs` (new). No workflow change: the existing test step runs the gate.
- Tests: `TacticusPlanner.GameCatalog.Tests` `DatamineSourceTests` (the gate) and `DatamineFilesTests` (path lookup, no leaf collision); manifest snapshot unchanged.
- Docs: `.agents/skills/game-catalog-data/SKILL.md` and its `.claude` mirror ("Updating to a new in-game version" rewritten around `REF`; `Data/curated/`); data-mines `docs/roadmap.md` marks step 4 done once this is archived.
- No EF Core migration, no endpoint or OpenAPI change, no infra change.
