# Proposal

## Why

The served `lre-battles` dataset names a wave enemy's level `stars`, but the value is V1's 1-based index into the NPC's stat ladder (`"{npcId}:{N}"`), not a star count: on the embedded data it runs from 1 to 56. The companion apps change `add-legendary-event-battle-preview` is the first client to read wave enemies (to show each enemy's level and stats), so the field is renamed now, before anything depends on the misleading name. V2 is pre-production, so the break is allowed.

## What Changes

- **BREAKING** (served shape): a wave enemy in `lre-battles` (and in the raw `lres-*.json` files it is copied from) is `{ id, ladderIndex, count }` instead of `{ id, stars, count }`. Values are unchanged.
- `GameCatalogRelease.SchemaVersion` is bumped, so clients re-sync the catalog.
- The `game-catalog-data` skill describes the field under its new name and meaning.

No range check is added: four Lysander boss entries point past their NPC's ladder today, and the client shows them as "Level unknown" (see the apps change).

## Capabilities

### New Capabilities

- `legendary-event-battles-dataset`: the served `lre-battles` record shape, how it is derived from the raw event files, and the meaning of a wave enemy's `ladderIndex`.

### Modified Capabilities

None.

## Impact

- Companion apps change: `add-legendary-event-battle-preview` in `tacticus-planner-apps` (its zod schema reads `ladderIndex`). This API half applies first.
- `src/TacticusPlanner.GameCatalog/Models/Lres.cs` (`GameCatalogLreEnemy`), the four `Data/lres/lres-*.json` files, `Models/GameCatalogRelease.cs`, `LreDenormalizerTests`, the manifest snapshot (`GameCatalogSnapshotTests...verified.txt`, schema version and `lre-battles` / `lres` hashes), `.agents/skills/game-catalog-data/SKILL.md` and its `.claude` mirror.
- No endpoint, EF Core migration or infra change.
