## Why

Campaign battles are the last catalog data copied from V1 by hand (`Data/campaign-battles/*.json`, 22 files).
The roadmap's step 6 left them for last because battle ids feed the API tests and the player-data transformer.

## What Changes

- Build every campaign group's battles from the pinned datamine `battles/campaigns.json`, with npc names,
  ranks and factions from `units/npc.json` and drop kinds from `upgrades.json`.
- Derive group metadata (group id, type, release type, core characters, faction, allies) from the campaigns'
  ids, kinds and unlock conditions. `Data/curated/campaign-curation.json` keeps only each group's datamine
  campaigns with V1's battle-id prefix, plus Indomitus's core characters (the tutorial names no units to unlock).
- Delete `Data/campaign-battles/`.
- Served shapes and every battle id are unchanged; no `SchemaVersion` bump. The `campaign-battles` and
  `campaign-definitions` hashes move; `characters` and `upgrades` keep theirs.

## Impact

- 37 battles take the game's current enemy power, 11 Indomitus tutorial battles (I01 to I14) list the game's
  enemy indices, I75's boss rank is Bronze1, and DAS28 has two Lascannons instead of three.
- Chaos allies gain `TheLostAndTheDamned` (the game allows it, and Lucius/Bile is playable). Allies follow the
  catalog's faction order, so three event pairs reorder (`["BlackTemplars", "Sisterhood"]`). Core characters
  follow the game's unlock order (`campaign2`: `blackHaarken` first).
