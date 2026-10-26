# Tasks

Companion apps change: none (no served shape changes). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 4 and `docs/catalog-audit.md`.

## 0. Decision gate

- [x] 0.1 The owner confirms design D1 (commit allowlisted raw copies to this public repo): confirmed 2026-10-26

## 1. Copy script and pin

- [x] 1.1 Add `scripts/pull-datamine.cs` per the spec (tag or SHA, `--source`, blob-filtered fetch, byte copy, prune, `files.sha256`, `REF`, summary, fail without changes on a missing path)
- [x] 1.2 Add `Data/datamine/files.txt` with `gameconfig/globalValues.json` and `gameconfig/items.json`, run the script against `v1.43.157.2989`, and commit `REF`, `files.sha256` and the two copies
- [x] 1.3 Run the script a second time to confirm it reports everything unchanged and leaves the tree clean

## 2. Loader

- [x] 2.1 Embed `Data/datamine/raw/**/*.json` with the `datamine/` logical name and remove it from the convention glob in `TacticusPlanner.GameCatalog.csproj`
- [x] 2.2 Make `GameCatalogLoader.OpenEmbeddedJson` skip `datamine/` resources
- [x] 2.3 Add `DatamineFiles.Open(rawPath)` (allowlisted paths only), and `DatamineFilesTests` for open by path, an unlisted path, and no leaf collision

## 3. Gate

- [x] 3.1 Add `DatamineSourceTests` (reads the source tree): `REF` format, `local` rejected, allowlist and copies agree, SHA-256 matches; one failing case per rule

## 4. Curated data

- [x] 4.1 `git mv` `event-definitions.json`, `event-occurrences.json`, `horde-mode-tiers.json`, `guild-raid-meta/` and `enums.json` into `Data/curated/`
- [x] 4.2 Confirm the manifest snapshot is unchanged with no `*.verified.txt` edit, and that `SchemaVersion` and `GameVersion` are unchanged

## 5. Docs

- [x] 5.1 Rewrite the skill's "Updating to a new in-game version" around `REF` and `pull-datamine` (keep the V1 notes for datasets not yet migrated), and add `Data/curated/` and the migration rules from design.md, in `.agents/skills/game-catalog-data/SKILL.md` and the `.claude` mirror
- [x] 5.2 Mention `scripts/pull-datamine.cs` in `AGENTS.md` (Project Structure)

## 6. Gates

- [x] 6.1 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`
- [x] 6.2 `dotnet build TacticusPlanner.slnx -c Release --no-restore`
- [x] 6.3 `dotnet test TacticusPlanner.slnx -c Release --no-build`
- [x] 6.4 `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .` (the copies ride along under `src/TacticusPlanner.GameCatalog/`; verified by the CI Build API job, which runs the same command)

## Deferred / out-of-session

- Verify the copies against upstream in CI (needs the cross-repo token from roadmap step 9).
- Mark roadmap step 4 done in `tacticus-planner-data-mines` `docs/roadmap.md` after archive.

## Workflow follow-up

- Archive this change in the PR before merge.
