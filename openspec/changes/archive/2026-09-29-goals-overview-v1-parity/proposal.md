## Why

V2's Goals list needs to show what a Character Ability goal still costs (gold
and ability badges), as V1 does. V1 derives that from a static
`characters-lvl-up-abilities` table that V2 has no equivalent of: the game
catalog serves the Machine of War ability cost ladder (`mow-upgrade-costs`)
but nothing for a character's ability levels, so the client cannot compute a
Character Ability goal's cost. This is the API half of the apps change of the
same name.

## What Changes

- Add one served catalog dataset, `character-ability-costs`: the shared ladder
  of gold and ability badges (with badge rarity) needed to raise a character
  ability to each level from 2 through the maximum, ported from V1's
  `characters-lvl-up-abilities.json`.
- Register it as a raw embedded source and a served dataset, with its
  denormalizer, endpoint (`GET /api/v1/game-catalog/character-ability-costs`,
  anonymous like the rest of the catalog), validation, and manifest hash entry.
- No EF Core migration, no database change, no change to existing datasets or
  to `SchemaVersion` (a new dataset is additive).

## Capabilities

### New Capabilities

- `character-ability-costs-dataset`: the served `character-ability-costs`
  dataset — its shape, level keying, badge rarity derivation, and validation.

### Modified Capabilities

(none)

## Impact

- `src/TacticusPlanner.GameCatalog/Data/character-ability-costs.json` (new raw
  source), `Models/GameCatalogDatasets.cs` (raw + served keys),
  `Models/` (raw record and served view), `Denormalization/`, `Validation/`,
  `GameCatalogLoader.cs`, `Models/GameCatalogSnapshot.cs`.
- `src/TacticusPlanner.Api/Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs`
  (one endpoint) and the generated OpenAPI artifact under `artifacts/openapi`.
- The public manifest and its snapshot test gain one entry.
- Companion apps change: `goals-overview-v1-parity` in
  `tacticus-planner-apps` (client dataset key, schema, and the goal cost
  calculation). This API half applies first.
