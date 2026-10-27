## Why

Campaign battles are the last catalog data copied from V1 by hand (`Data/campaign-battles/*.json`, 22 files).
The roadmap's step 6 left them for last because battle ids feed the API tests and the player-data transformer.

## What Changes

- Build every campaign group's battles from the pinned datamine `battles/campaigns.json`, with npc names,
  ranks and factions from `units/npc.json` and drop kinds from `upgrades.json`.
- Keep group metadata (group id, faction, release type, allies, core characters) and, per Tacticus type, the
  datamine campaign and battle-id prefix in `Data/curated/campaign-curation.json`.
- Delete `Data/campaign-battles/`.
- Served shapes and every battle id are unchanged; no `SchemaVersion` bump. Only the `campaign-battles`
  hash moves; `campaign-definitions`, `characters` and `upgrades` keep theirs.

## Impact

- 37 battles take the game's current enemy power, 11 Indomitus tutorial battles (I01 to I14) list the game's
  enemy indices, I75's boss rank is Bronze1, and DAS28 has two Lascannons instead of three.
