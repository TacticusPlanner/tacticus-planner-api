# Design

## Context

See proposal.md. `GameCatalogLreEnemy(string Id, int Stars, int Count)` in `Models/Lres.cs` is both the raw deserialization record and the served wave enemy (`LreDenormalizer.BuildLreBattles` passes `battle.Waves` through). The companion apps change is `add-legendary-event-battle-preview` in `tacticus-planner-apps`.

## Goals / Non-Goals

**Goals:** the raw files, the model and the served payload all say `ladderIndex`.

**Non-Goals:** validating the index against the ladder; changing any value; reshaping waves.

## Decisions

### D1. Rename in the raw files too, not only the served projection

Raw `lres-*.json` and the served record share one C# record. Renaming the property and the raw keys keeps one name end to end. Alternative: `[JsonPropertyName("stars")]` on the raw side and a separate served record; rejected as two names for one value and an extra mapping, for a field no other code reads. The raw edit is a scripted key rename (`json.dumps(..., indent=2, ensure_ascii=False) + "\n"`, LF) so values and formatting are untouched; a test asserts no raw enemy keeps a `stars` key.

### D2. Name: `ladderIndex`

`progressionIndex` would match the guild raid API's 1-based index, but served NPC stat rows already have a `progressionIndex` field meaning something else (0–13 star step), and the apps code reads both side by side. `ladderIndex` names what it indexes and avoids the clash.

### D3. Schema version

A served field rename is a breaking shape change, so `GameCatalogRelease.SchemaVersion` goes from 5 to 6 (per the `game-catalog-data` skill). The manifest snapshot changes in `schemaVersion` and the `lres` / `lre-battles` hashes (`lres` because its raw source bytes change, if its hash covers them; the snapshot shows which).

### D4. No range validation

Four embedded boss entries exceed their ladder, and V1 had the same data. Failing the load would block the catalog on data the client already handles; tightening it waits for a datamine refresh that fixes those entries.

## Risks / Trade-offs

- [The apps client still parses `stars`] → the apps half of this change updates `lreEnemySchema` to `ladderIndex`; the API half applies first and the apps half ships right after. Until then the client's zod parse of `lre-battles` would fail, so both PRs merge together.
- [Future datamine refresh reintroduces `stars`] → the skill's refresh section names the new key, and the raw-key test fails on `stars`.

## Migration Plan

No EF Core migration. Deploy the API and the apps change together; clients re-sync on the schema version bump. Rollback is a revert of both.
