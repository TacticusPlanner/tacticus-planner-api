## Why

The NPC Library can say what an enemy *is* but not *where a player meets it*
([apps#158](https://github.com/TacticusPlanner/tacticus-planner-apps/issues/158)), and its
game-mode filter ([apps#156](https://github.com/TacticusPlanner/tacticus-planner-apps/issues/156))
was pulled because the only mode signal is a guessed id suffix. The pinned datamine already lists the
enemies of nearly every battle: 353 of the 472 regular NPC units are named by at least one battle in
datamine `1.43.157.2989`. But that knowledge is scattered across eight sources, four of which the catalog does
not serve at all (Survival, Onslaught waves, and the encounter roles inside guild raids). The catalog
should invert it once, server-side, into one small dataset a client can read without loading half the
catalog.

## What Changes

- New served dataset `npc-encounters`: one record per (NPC, source, group) naming every place the
  NPC fights as an enemy, with the specific battles, tiers, tracks or rounds. Sources are Campaign,
  Campaign Event, Legendary Event, Quest Event, Incursion, Guild Raid, Survival and Onslaught.
- Campaign, Legendary Event, Quest Event, Incursion and Guild Raid encounters are derived from the
  already-built served views (`campaign-battles` / `campaign-definitions`, `lre-battles`,
  `quest-events`, `incursions`, `raid-bosses`), so every battle or group id in an encounter resolves
  in the dataset a client would link to.
- Survival encounters are read from the `survival_*` battle sets of `gameconfig/battles/battleSets.json`.
  Onslaught encounters are read from `tierWaves` in `gameconfig/battles/onslaught.json`. Both files are
  already in the datamine allowlist, so they need no new raw data.
- Load-time validation: every NPC, group and battle id an encounter names must resolve, or the load fails.
- Ids and numbers only. The client resolves names, labels, links and event timing (through the
  existing `events-calendar` and `lres` datasets). The server sends no schedule or "currently available" flag.
- Sync PvP and tutorial battles are deliberately not indexed. Players never choose these modes.

No existing dataset changes shape. No EF Core migration.

Companion change: `add-npc-encounters` in `tacticus-planner-apps` (NPC Library encounter section,
game-mode filter, variation label fixes). This API half applies first.

## Capabilities

### New Capabilities
- `npc-encounters-dataset`: the served `npc-encounters` dataset: its record shape, how each source's
  encounters are derived from served views or raw files, ordering, which NPCs are indexed, and the
  load-time validation.

### Modified Capabilities
<!-- None: existing datasets keep their shapes; npc-encounters only reads them. -->

## Impact

- `src/TacticusPlanner.GameCatalog`: a new `NpcEncounterDenormalizer` (plus small Survival and
  Onslaught readers), models, a `GameCatalogDatasets.Served` entry, validation, and the manifest
  (new dataset hash; derived `schemaHash` moves).
- `src/TacticusPlanner.Api/Features/GameCatalog`: one new anonymous `ServedDatasetEndpoint` at
  `GET /api/v1/game-catalog/npc-encounters`. The regenerated `artifacts/openapi` artifact gains it.
- Tests: denormalization coverage per source, validation failures, manifest snapshot.
- `.agents/skills/game-catalog-data/SKILL.md`: a short entry for the new dataset.
- Consumer: `tacticus-planner-apps` `add-npc-encounters`.
