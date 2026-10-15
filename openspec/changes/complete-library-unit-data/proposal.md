## Why

The Library unit pages (character #206, Machine of War #205) work around missing game data: the MoW
Mythic panel has no ability to show, and the character page reads mythic quests from a static,
English-only copy that the apps bundle ships as translation files. Issue
[#204](https://github.com/TacticusPlanner/tacticus-planner-apps/issues/204) asks the game catalog to serve
this data. V1 already holds both: one mythic ability per MoW (`mow/data/new-mows-data2.json`) and the quest
data for 117 characters (`mythic_quests/data/mythic-quests.json`).

## What Changes

- Add an optional `mythicAbility` (`{ name }`, the ability id) to each raw `mows[]` record in
  `units-{faction}.json`, ported from V1's `mythicAbilities[0]`, and serve it on the `mows` dataset. All 11
  MoWs get one.
- Add a new served dataset `mythic-quests`: per character, its five quests with their tasks (loca key,
  target, parameters) and rewards, parsed from V1's reward strings into structured records. Built from a
  new raw `mythic-quests.json`, validated at load.
- Bump `GameCatalogRelease.SchemaVersion` (a served shape changes and a dataset is added) and `Version`.
- Update the `game-catalog-data` skill's refresh procedure: the `mows` mapping gains the mythic ability
  from `new-mows-data2.json`, a new `mythic-quests` mapping, and the client-side companion step gains the
  game-text generator re-run, relic ability icons and exact-case asset filenames.

Companion change: **`complete-library-unit-data`** in `tacticus-planner-apps` (consumes both datasets,
ports relic and MoW ability rules text, fixes item icons). This API half applies first.

## Capabilities

### New Capabilities

- `mythic-quests-dataset`: the served `mythic-quests` dataset: shape, reward parsing, ordering and load
  validation.
- `mows-dataset`: the served `mows` dataset's MoW mythic ability field (the dataset itself predates specs;
  this capability starts with the field this change adds).

### Modified Capabilities

(none)

## Impact

- `src/TacticusPlanner.GameCatalog`: `Data/units/units-*.json` (11 `mows[]` records), new
  `Data/mythic-quests.json`, `Models/Mows.cs`, a new mythic quests model + denormalizer + validation,
  `GameCatalogDatasets` (raw and served keys), `GameCatalogRelease`.
- Public manifest gains a `mythic-quests` hash; the `mows` hash changes. The API-level manifest snapshot
  test (`GameCatalogSnapshotTests`) must be promoted.
- New public endpoint `GET /api/v1/game-catalog/mythic-quests` (same catalog route pattern, no auth); the
  OpenAPI artifact under `artifacts/openapi` is regenerated if the catalog routes are enumerated there.
- No database or EF Core migration.
- `.agents/skills/game-catalog-data/SKILL.md` and its `.claude/skills/` copy.
