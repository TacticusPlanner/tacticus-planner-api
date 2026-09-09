## Why

The Guild Raid Boss Library has authoritative game and season data but no
maintained source for strategy recommendations. A centrally validated, public
catalog dataset is needed so clients can present current Meta and alternate
teams, their Comp archetypes, and evidence without hard-coding editorial data
in a page.

## What Changes

- Add a manifest-synced, anonymous `guild-raid-meta` game-catalog dataset for
  curated Guild Raid Boss strategy data.
- Author ordered Comp profiles and boss recommendations using only stable boss,
  character, and Machine of War ids; each recommendation has an exact five-hero
  team, one Machine of War, one or more Comp ids, and optional replay evidence.
- Include source and update identifiers as structured metadata while leaving all
  visible labels, portraits, and outbound links to the client.
- Validate all cross-references and recommendation invariants at catalog load
  time; an editorial-data update changes only this dataset's manifest hash.

## Capabilities

### New Capabilities

- `guild-raid-meta-dataset`: Serves validated, id-only curated Guild Raid Boss
  Meta recommendations and reusable Comp guidance through the game catalog.

### Modified Capabilities

_None._

## Impact

- `TacticusPlanner.GameCatalog`: raw data, models, dataset registry, loader,
  denormalization, validation, manifest hash, and snapshot coverage.
- `TacticusPlanner.Api`: a new anonymous game-catalog dataset endpoint and
  generated OpenAPI contract.
- Companion `add-guild-raid-meta-catalog` change in `tacticus-planner-apps`:
  schema validation, IndexedDB sync, query access, and id-based presentation
  resolution. Apply this API change before its companion.
