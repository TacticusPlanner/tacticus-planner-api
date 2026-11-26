## Context

See `proposal.md` for the motivation and the specs for the behavior. Companion change: `add-survival-events` in `tacticus-planner-apps`.

- The game ships each seasonal event in `liveEvents/seasonalEvent.json`; five of them (May, September and December 2026, Crescendo A and B) have `includesSurvival` and a `survival` block naming a `survival_*` battle set.
- The event shop dataset already reads the same reward ladders (`tieredProgressRewards`), chests and seasonal quest groups.
- Teams already have mode-specific fields (`campaignGroupId`, `incursionMowIds`) checked by `TeamRules` against plain catalog facts.

## Decisions

### D1: One record per seasonal event with Survival, keyed by the event name

The id is the event name without `season_` and `_event`, with `_` as `-` (`december-2026`, `crescendo-a`), like the event shop ids. Records are ordered by id; the client orders by date. The game data says nothing about dates, so a calendar `survival` occurrence with `survivalEventId` dates a record; an undated record has null `definitionId`, `startUtc` and `endUtc`.

### D2: Reuse the event shop readers

Milestones are the `rewardProgressId` ladder and missions the seasonal quest groups by the id's prefix (`december_2026_daily_01`, `crescendo_b_premium_chain_01`), both through the event shop readers, which now take the dataset they report a load failure for. Chests are the `chest` products named `product_<prefix>_progress_chest_NN` that cost the event currency and whose prefix contains every part of the event's own (`crescendo_event_a` for Crescendo A, `seasonal_event_may_2026` for May), in `NN` order.

### D3: Waves are served grouped, ids only

A wave's army is grouped by `npcId:N` in first-seen order with a count. Power-ups keep the game's id; a summon (`powupSummonSpawn_<unit>:N`) also names its NPC and stats row, because the summon NPC has a stats ladder and `N` climbs with the run. Medals are read by name (`ironMedalWave`, `ironMedalReward`, ...) for every medal the event has. `CLONE_WAVE` in the kill scores becomes `cloneWaveScore`.

### D4: Allowed factions, not disallowed

The battle lists the factions it bars. The record serves every served faction it does not bar, in the factions dataset order, so the client and the team rules read one list. A barred faction the catalog does not serve fails the load.

### D5: A Survival team is a Horde team with a `survivalEventKey`

The key is a served `survival-events` id. The featured hero always fights and is never stored. The cap is the record's `maxCharacters` (10), counted over every stored character (Survival has no flex). MoWs are refused when the record's `mowsAllowed` is false; there is no Survival MoW cap of its own, because no event has ever allowed MoWs and the game publishes none. A team keeps its key after its event ends and is only labelled by it in lists, never hidden.

### D6: Prices are the US store price in cents

`realMoneyProducts.json` holds one price per product. The client formats it in dollars.
