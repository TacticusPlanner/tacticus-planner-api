# Tasks

Companion apps change: `add-legendary-event-battle-preview` in `tacticus-planner-apps`; this API half applies first and both merge together.

## 1. Rename

- [x] 1.1 Rename `Stars` to `LadderIndex` on `GameCatalogLreEnemy` in `Models/Lres.cs` with a doc comment stating the 1-based ladder meaning; verify `dotnet build TacticusPlanner.slnx` succeeds.
- [x] 1.2 Rename the `stars` key to `ladderIndex` on every wave enemy in the four `Data/lres/lres-*.json` files with a script that preserves values and formatting; verify `git diff --stat` shows only those keys changed and the catalog loads on startup.
- [x] 1.3 Bump `GameCatalogRelease.SchemaVersion` from 5 to 6; verify the manifest snapshot test fails only on `schemaVersion` and the changed hashes, then accept the new snapshot.

## 2. Tests and docs

- [x] 2.1 In `LreDenormalizerTests`, assert the served `astarLysander-alpha-1` first wave is `{ id: "orksNpc1Grot", ladderIndex: 4, count: 7 }`, that no served or raw enemy has a `stars` key, and that the Helbrecht entry past its ladder still loads; verify `dotnet test tests/TacticusPlanner.GameCatalog.Tests` passes.
- [x] 2.2 Update the `game-catalog-data` skill (`.agents/skills` and the `.claude/skills` mirror): wave enemies are `"{npcId}:{N}"` aggregated to `{id, ladderIndex, count}`, with `ladderIndex` the 1-based stat ladder position; verify both copies match.

## 3. Gates

- [x] 3.1 `dotnet build TacticusPlanner.slnx`, `dotnet test tests/TacticusPlanner.GameCatalog.Tests`, `dotnet test tests/TacticusPlanner.Api.Tests` and `git diff --check` pass.

## Workflow follow-up

- Archive after review together with the apps half.
