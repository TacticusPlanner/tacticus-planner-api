# Design

## Context

At `v1.43.157.2989`:

- `lineup.json` holds all 130 units (119 characters, 11 MoWs) keyed by id. Field by field, they match the V1-ported `units-*.json`, with one exception: the damage-profile lists, which V1 sorted.
- `npc.json` holds the same 539 NPC ids as `npcs-*.json`. It differs in four ways: newer stat ladders for 55 NPCs, game ability order for 14, one movement value, and V1's unsorted order in three faction files.
- Every unit's traits start with `Hero`, which V1 dropped.
- Damage profiles come from each ability's `constants.damageProfile` / `damageProfile_N` in `abilities.json`.

## Decisions

### D1. Batch three datasets, keep every shape

The datasets are characters, mows and npcs. No field is renamed, so the characters and mows hashes prove the migration by not moving. The npcs hash move is all game data, listed in the proposal.

### D2. Sort damage profiles, keep ability order

V1 sorted each unit's damage-profile list. Sorting them keeps characters identical, and the list is a set for the client. Ability ids keep the game order, which V1 also used everywhere except 14 NPCs.

### D3. Order units by id within a faction

The retired files were id-ordered for every units file and 18 of 21 NPC files. Ordering by id everywhere is deterministic and independent of the JSON object order.

### D4. No faction display names

The raw files carried a faction display name (e.g. `T'au Empire`). It was never served, and the datamine has it only in `languages/*.json`, which stay out of the API. The record keeps its `Name` slot, filled with the faction id, so validation and callers are unchanged.

### D5. Fail on shapes the reader cannot map

A ranged first weapon, more than two weapons, an unknown ability id, a MoW without exactly two actives or without recipes, and more rank-up rows than ranks all fail startup naming the unit. The game data has none of them today.
