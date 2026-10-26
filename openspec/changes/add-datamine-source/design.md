# Design

## Context

- `TacticusPlanner/tacticus-planner-data-mines` (private) holds the latest PC build's config under `raw/` (`raw/VERSION` is `1.43.157.2989` today). Merging a datamine PR tags the merge commit `v<version>` and releases it. A forced re-run of the same version **moves** the tag to a new merge commit, so a tag alone is not a stable pin. Its `AGENTS.md` makes the raw layout a contract: files are only split, never renamed or edited, and moving one breaks consumers.
- Its `docs/catalog-audit.md` (2026-10-26, against `1.43.157.2989`) maps each API `Data/**` file to a datamine source, lists what stays curated, and lists apps constants that move behind new datasets. Its `docs/roadmap.md` orders the work: 1 to 3 are done (the per-key split, the apps quick fixes, and the catalog speed-ups in #121); this change is step 4.
- The loader finds raw files by **leaf name** (`OpenEmbeddedJson` matches `.{leaf}` with `SingleOrDefault`). The datamine's leaf names include `units.json`, `factions.json`, `upgrades.json`, `items.json` and `abilities.json`, and several repeat across folders (`globalconfig/guildBoss/units.json` and the `units/` folder). Embedding them under the same convention would throw or bind the wrong file.
- The API repo is public (AGPL-3.0). data-mines is private.
- Companion apps change: none. The contract surface (served datasets and manifest) is unchanged.

## Goals / Non-Goals

**Goals:**

- One committed, reviewable pin of the datamine, and a one-command refresh.
- Raw copies identical to upstream, with a CI check that catches hand edits and a forgotten `local` pin.
- Denormalizers can read a datamine file by its upstream path with no ambiguity.
- Leave every served dataset and the manifest exactly as they are.

**Non-Goals:**

- Moving any dataset onto the datamine. Each one is its own change, starting with the `equipment` pilot.
- Automating the bump. Roadmap step 9 does that; it needs a cross-repo token.
- Game text and sprites (roadmap steps 8 and 10).
- Exposing the datamine version in the manifest. That would change the manifest shape; `GameVersion` stays hand-set until a migrated dataset needs it.

## Decisions

### D1. Copy into the repo, not a submodule or a build-time fetch

The copies are committed under `Data/datamine/raw/` and embedded like the rest of `Data/**`. The build, the Docker image and outside contributors then need no access to the private repo, and a bump PR's diff is exactly the raw change the API consumes.

- _Alternative: git submodule._ Rejected. Cloning, CI and the Docker build would all need a token for a private repo, and the API would carry about 800 MB (mostly sprites) it does not read.
- _Alternative: fetch at build time._ Rejected for the same token problem, and because the build would stop being reproducible from the repo alone.
- **Visibility (needs the owner's confirmation):** committing copies publishes the allowlisted files. They are game config the public V1 repo already published in its own form, and the served catalog already derives from them. One real difference: raw `units/lineup.json` lists units the game has not released yet (the catalog skips them when it serves). Stripping them on copy would break the "unchanged copy" rule, so the recommended answer is to accept it. If the owner disagrees, the fallback is a build-time fetch (D1's second alternative) with a token in CI and Docker.

### D2. `REF` pins tag and SHA, and the SHA wins

`Data/datamine/REF` is one line: `<tag> <40-hex sha>`, for example `v1.43.157.2989 e5c8c9bc749a7d1bbfb33a3d93056e8826e82678`. The script resolves a tag argument to its current SHA and records both, so a later tag move does not silently change what a rebuild means. During development, `local <path>` records that the copies came from a working tree. CI rejects it.

### D3. An allowlist, not a mirror

`Data/datamine/files.txt` lists `raw/`-relative paths (sorted, no globs, `#` comments allowed). Only files a denormalizer reads are copied. This keeps the public footprint small (D1), keeps bump diffs readable, and leaves out `languages/` (11 MB) until step 8 decides how game text is served. Each migration change adds its own lines. This change seeds the pilot's two files:

```
gameconfig/globalValues.json   # itemAscensionCosts (equipment upgrade ladder)
gameconfig/items.json          # equipment
```

### D4. A C# file-based app for the copy

`scripts/pull-datamine.cs` runs with `dotnet run scripts/pull-datamine.cs -- <tag|sha>` (the repo's only required toolchain is the .NET SDK; `global.json` pins 10.0.402, which runs file-based apps). It uses `git` with the caller's own credentials: `git clone --filter=blob:none --no-checkout` into a temp directory, a sparse checkout of the allowlisted paths at the SHA, then a byte copy. With `--source <path>` it reads a local data-mines checkout (the workspace's sibling folder) and writes a `local` pin. It then deletes copies that are no longer listed, writes `files.sha256` (`<sha256>  <path>`, sorted, LF), and prints the upstream `raw/VERSION` and a per-file added/changed/removed summary for the PR description.

- _Alternative: a `gh api` shell script._ Rejected because it needs `gh`, bash and jq on Windows dev machines, and the contents API caps file size at 100 MB.

### D5. Datamine files get their own resource namespace

The csproj embeds `Data/datamine/raw/**/*.json` with `LogicalName="datamine/%(RecursiveDir)%(Filename)%(Extension)"` (backslashes normalized to `/`) and removes them from the convention glob. `OpenEmbeddedJson` skips any resource that starts with `datamine/`. The new `DatamineFiles.Open(string rawPath)` returns a stream for an exact raw path and throws for a path not in the allowlist. Denormalizers then say exactly which upstream file they read (`DatamineFiles.Open("gameconfig/items.json")`), which gives the 1:1 trail the roadmap asks for. It streams, so the #121 speed-ups (source-generated contexts, deserialize from the stream) apply unchanged.

### D6. The CI gate runs as a test

The checks (`REF` format, not `local`, allowlist and copies agree, SHA-256 matches) live in `TacticusPlanner.GameCatalog.Tests` as `DatamineSourceTests`. They read the source tree, not the embedded resources, so a hand edit fails `dotnet test` locally and in CI with no new workflow step. `_build-api.yml` already runs the tests. The `changes` filter in `ci.yml` treats `src/**` as code, so a bump PR always builds.

### D7. `Data/curated/` is a move only

The loader matches by leaf name, so moving the curated files changes no resource suffix, no hash and no snapshot. The folder marks which data has no game source, so a reviewer knows not to expect a datamine binding for it. `drop-chances.json` stays where it is: its fraction is game data (battle `loot.chanceOf`) and only `effectiveRate` is curated, which its own migration splits.

## Migration rules for later changes (recorded in the skill)

- One dataset per change. Either keep the served shape (the manifest snapshot shows no hash moved except where the game data really differs, and the PR explains each one) or rename to the game's terms (bump `SchemaVersion` and ship the apps companion), never both in one change.
- Ids that user data stores (battle ids, LRE ids, mission indices, goal and plan references) need a data migration when renamed. V2 is pre-production, so this is allowed but never local.
- Order, easiest first (roadmap step 6): `equipment` (pilot); then cost and progression tables (`character-ability-costs`, `mow-upgrade-costs`, `ascension-costs`, `unlock-shard-costs`), `upgrades`, `npcs`, characters and MoWs, `unit-power`, `raid-bosses`, `shops`, `mythic-quests`, HSE, `onslaught-rewards`, `quest-events` and `quest-crates`, LRE, and `campaign-battles` last.
- New datasets named after their game source (step 7): `progression-rules`, `global-values`, `factions`, `damage-types`, `hre`.
- Stays curated (audit §1): `event-definitions`, `event-occurrences`, LRE `runStartDatesUtc`, `drop-chances` `effectiveRate`, `horde-mode-tiers`, `guild-raid-meta`, quest-event `power`, `enums.json`.

## Risks / Trade-offs

- [The datamine renames or moves a file] → its `AGENTS.md` forbids that. If it happens anyway, `pull-datamine` fails on the missing allowlisted path, and the bump PR shows it.
- [The tag moves after a forced re-run] → `REF` keeps the old SHA until someone bumps it on purpose (D2).
- [Unreleased content becomes public] → see D1. The owner decides before apply.
- [Leaf collision regressions] → `DatamineFilesTests` asserts that the convention loader opens no `datamine/` resource.
- [Repo growth] → only allowlisted files are copied, and git stores each bump as a delta. The full migration needs about 15 MB of JSON.

## Migration Plan

Additive. No served data changes, so nothing is deployed differently. Apply order: this change, then the `equipment` pilot. No apps companion.

## Open Questions

- **Visibility (D1).** Is committing allowlisted raw files to this public repo acceptable? Recommended: yes.
- **Datamine stability.** The data-mines README notes that its download client uses an undocumented launcher API. If it breaks, `REF` simply stays pinned, so the API is unaffected.
