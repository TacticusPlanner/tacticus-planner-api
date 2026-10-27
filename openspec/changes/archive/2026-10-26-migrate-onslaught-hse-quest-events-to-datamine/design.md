# Design

## Context

At `v1.43.157.2989`, compared against the retired files:

- `onslaught.json` `honorYourHeroesRewards` has rows for progression indexes 1, 2, 4, 5, 7, 8, 10, 11, 14, 15, 18, 19 in each of the 21 tiers. V1's `regular` ranges are the shard ranges at 2, 5, 8, 11 and 14 and its `mythic` range is the one at 19, for all 21 tiers. V1's `badges` are the per-round `tierWaves` badge rewards summed per rarity, for 20 tiers; Bronze-1 differs by one Common badge.
- Running the retired `import-hse-scoring.py` mapping over the datamine (instead of V1's per-event files) reproduces `home-screen-event-scoring.json` except `hse-defeat-waves` `default`, whose ladders the game has since replaced; its `hse_defeat_waves_tier_low` ladder is gone from `tieredProgressRewards.json`.
- `linearHeroEvent.json` holds five Quest Events; the three in `quest-events.json` convert to it exactly. Their crate tracks differ per event.

## Decisions

### D1. Keep every shape

No field is renamed. Each hash move is game data, listed in the proposal.

### D2. Port the HSE import, not its output

The import's mapping table and normalization rules move into `EventDatamineReader` unchanged, so a refresh is a `REF` bump. The C# output was checked equal to the script's output over the same datamine.

### D3. Skip a tier without a ladder

The Defeat Waves Low tier's event entry still exists but points at a ladder the game removed. Serving V1's old Low ladder next to the game's new High and Mid ones would mix two runs, and failing startup would block every refresh, so the tier is skipped.

### D4. No battle power

The game config has no enemy power, so the dataset serves none. Clients estimate a battle's power from its enemies and the npc power ladders.

### D5. Crates wait for a shape change

The served `quest-crates` is one shared track, but the game has one per event. Choosing one event's track would be wrong for the others, so the crates stay hand-kept until a change serves them per event (a `SchemaVersion` bump with an apps companion).
