## Why

The V2 Library has a `/library/npcs` route that still renders a "detail unavailable" placeholder (tacticus-planner-apps#43 asks for the V1 `learn/npcs` page to be reimplemented there). The served `npcs` dataset cannot back that page: `NpcDenormalizer.BuildNpcs` flattens the per-faction raw files and discards the faction and alliance each NPC belongs to, and nothing distinguishes real units from the Machines of War and loot objects that ride along in the same list. The client therefore cannot offer a faction filter, faction disambiguation in a picker, or a reliable "units only" scope without pattern-matching NPC ids.

## What Changes

- Each served `npcs` record gains `factionId` and `alliance`, carried over from the raw per-faction file the NPC was loaded from (`npcs-objects.json` contributes `Objects` / `Neutral`).
- Each served `npcs` record gains a `kind` field — `unit`, `machineOfWar`, or `object` — derived at denormalization time so the client never classifies by id pattern.
- The NPC dataset gets its own `npcs-dataset` capability spec describing the served projection (it has none today; the shape is only implied by `Models/Npcs.cs`).
- The dataset remains a flat list in the same key, endpoint, and envelope; the change is additive (no `SchemaVersion` bump), but the `npcs` dataset hash and the manifest snapshot change.
- Companion change: `add-npc-library-page` in `tacticus-planner-apps` consumes the new fields to build the NPC Library page. This API half applies first.

## Capabilities

### New Capabilities
- `npcs-dataset`: the served `npcs` catalog dataset — its record shape (identity, faction/alliance, kind, weapons, ability/trait ids, stat ladder), how `factionId`/`alliance`/`kind` are derived from the raw per-faction sources, and the id-only display contract.

### Modified Capabilities
- (none — no existing spec covers the NPC dataset)

## Impact

- `src/TacticusPlanner.GameCatalog/Models/Npcs.cs` — `GameCatalogNpc` gains `FactionId`, `Alliance`, `Kind`.
- `src/TacticusPlanner.GameCatalog/Denormalization/NpcDenormalizer.cs` — `BuildNpcs` stamps the faction/alliance from the owning `GameCatalogFactionNpcs` and classifies `kind`.
- `tests/TacticusPlanner.GameCatalog.Tests` — new `NpcDenormalizerTests` covering the derivation; `tests/TacticusPlanner.Api.Tests/GameCatalogSnapshotTests.*.verified.txt` — `npcs` hash changes.
- `artifacts/openapi/TacticusPlanner.Api.json` — regenerated on build; `GameCatalogNpc` gains three properties.
- Consumers: `tacticus-planner-apps/packages/game-catalog` (`npcSchema` is a loose object, so the extra fields pass through until the companion change tightens it); the raid-boss library resolves field NPCs from this dataset and is unaffected by additive fields.
- No EF Core migration — the game catalog is embedded static data, not database state.
