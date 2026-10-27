## Why

Step 6 of the data-mines roadmap moves the catalog off hand-kept V1 files. The four `lres-*.json` files are the
last event data copied by hand. They have drifted from the game: Farsight's and Uthar's chest ladders stop at
54 chests, Uthar's premium missions are Farsight's, Chaos tracks miss The Lost and the Damned, and several
battle powers are stale.

## What Changes

- Build the `lres` and `lre-battles` datasets from the pinned datamine (`liveEvents/legendaryHeroEvent.json`,
  `battles/battleSets.json`, `quests/groups.json`, `loot/tieredProgressRewards.json`, `shop/segments.json`,
  `shop/products.json`, `loot/chests.json`, `units/heroProgressionStepsPerUnit.json`).
- Derive what V1's datamine pipeline derives: the event that unlocks the unit, its name, battle completion
  points (one per spawned enemy), track names, enemy labels and links, allowed-units filters and restriction
  names (V1's `buildTrackName` and `objectiveDisplayName`). Keep only run starts in
  `Data/curated/lre-curation.json`; its keys pick the served events.
- Generate mission lines from the game's quest tasks with V1's wording, so the existing parser and validation
  keep working.
- Delete `Data/lres/*.json` and `GameCatalogDatasets.LreEvents`.
- Served shapes are unchanged; no `SchemaVersion` bump. The `lres` and `lre-battles` hashes move.

## Impact

- `lre-battles`: battle and wave powers follow the game (Lysander, Uthar and the Chaos tracks), and Chaos
  tracks also disallow `TheLostAndTheDamned`.
- `lres`: Farsight's and Uthar's chest ladders grow to the game's 60 chests; Uthar's premium missions and
  Farsight's last premium missions follow the game. Plans store mission ticks by index, so ticks on those
  lists now point at the game's missions.
