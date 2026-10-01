## Why

The apps change `add-daily-raids-filters` ports V1 Raids Filters, whose "Allies" section filters campaign locations by the allied alliance and factions a player may deploy there. V1 derives allies from a hardcoded per-campaign table (`CampaignsService.getEnemiesAndAllies`). The served `campaign-battles` view carries only the enemy side (`enemiesAlliances`, `enemiesFactions`), so the V2 client has no source for allies. Per product decision the data belongs in the catalog, not in a hand-maintained client table.

## What Changes

- Author `alliesAlliance` (string, an `Alliance` value) and `alliesFactions` (string array of faction ids) on each campaign group in `Data/campaign-battles/campaign-battles-*.json` (22 files), ported from V1 `getEnemiesAndAllies`. Allies are per campaign group, identical for every battle in the group (V1 has no per-node allies).
- Add `AlliesAlliance` / `AlliesFactions` to the raw `GameCatalogCampaignGroup` model and to the served `GameCatalogCampaignBattleView`; the denormalizer copies them from the group onto each battle. `campaign-definitions` is unchanged.
- Validate at load: `alliesAlliance` is a known alliance, `alliesFactions` is non-empty and every id is a known faction.
- Additive served-shape change: `SchemaVersion` is NOT bumped (the apps zod schemas are loose objects and other consumers ignore unknown fields). Only the `campaign-battles` dataset hash and `sourceHash` move; promote the manifest snapshot.
- Companion change (same name) in `tacticus-planner-apps`. **API applied first**: the apps schema marks the new fields required.

## Capabilities

### New Capabilities

- `campaign-battles-dataset`: the served campaign battle view exposes the allied alliance and factions of its campaign group.

### Modified Capabilities

None.

## Impact

`Data/campaign-battles/*.json`, `Models/Campaigns.cs`, `Denormalization/CampaignDenormalizer.cs`, `Validation/*` (required-fields and reference checks), `TacticusPlanner.GameCatalog.Tests`, the manifest Verify snapshot (`GameCatalogSnapshotTests`), and any API test fixture that constructs a battle view. No endpoint route, EF Core, or migration change; the OpenAPI artifact is reviewed if the battle view is part of the contract.
