## Purpose

Defines the served `survival-events` catalog dataset: each seasonal event's Survival mode, rewards, missions and offers, which the Survival planner reads.

## ADDED Requirements

### Requirement: The survival-events dataset serves one record per seasonal event with Survival

The catalog SHALL serve `survival-events` at `/api/v1/game-catalog/survival-events` in the standard dataset envelope and list it in the manifest. It SHALL hold one record per entry of the datamine `liveEvents/seasonalEvent.json` whose `includesSurvival` is true, ordered by `id`. The `id` SHALL be the event name without `season_` and `_event`, with `_` replaced by `-`.

Each record SHALL carry:

- `id`, `eventKey` (the event name), `featuredCharacterId`, `currency` (the event's `resourceId`)
- `definitionId`, `startUtc`, `endUtc`: from the calendar occurrence of a `Survival` definition whose `survivalEventId` is the record's id; null when there is none
- `rules`: `allowedFactionIds` (every served faction the battle does not disallow, in factions order), `mowsAllowed`, `maxCharacters` (10), `stamina` `{ start, max, regenSeconds, regenAmount }`, `statBoostPct`, `statBoostStats`, `lastStand` `{ buffId, strategicMultiplier }` when the event uses Last Stand, `summon` `{ unitId, levels: [{ rarityCap, abilityLevel }] }` when the event has a global summon ability, `persistentRarityUps` `[{ rarity, wave }]`, `featuredHeroStats` `[{ rarity, critChance, critDamage, blockChance, blockDamage }]`
- `waves` (run order): `{ wave (1-based), round, power, score, cloneHealthPct (clone waves only), enemies: [{ npcId, statsIndex, count }], enemyRarityOnClear (when set), powerUpsOnClear: [{ id, npcId and statsIndex (summons only), count }] }`
- `scoring`: `medals` `[{ medal, wave, rewards }]` (iron to adamantine, those the event has), `killScores` `[{ npcId, statsIndex, score }]`, `cloneWaveScore`, `skippedTurnScore`, `rarityMilestones` `[{ rarity, rewards }]`
- `points`: `{ max, freeMissions, premiumMissions, survivalFree, survivalPremium }`
- `milestones` (the `rewardProgressId` ladder), `missions` (daily, free and premium chains by the id's prefix) in the event shop shapes
- `chests` `[{ level, cost: { type, amount }, rewards }]` and `offers` `[{ id, kind, priceCents, maxPurchases, rewards }]`

#### Scenario: Crescendo events take their own chests and missions

- **GIVEN** Crescendo A and B share a currency and the products `product_crescendo_event_a_progress_chest_01..35` and `product_crescendo_event_b_progress_chest_01..35`
- **WHEN** the dataset is read
- **THEN** `crescendo-a` has the 35 `_event_a_` chests and `crescendo-b` the 35 `_event_b_` chests, and `crescendo-b`'s daily missions start at `crescendo_b_daily_01`

#### Scenario: A clone wave has no enemies

- **WHEN** a wave has `isCloneWave`
- **THEN** it has `cloneHealthPct` and no enemies

#### Scenario: An undated event

- **GIVEN** no calendar occurrence names `crescendo-a`
- **THEN** its `definitionId`, `startUtc` and `endUtc` are null

### Requirement: A Survival event that does not hold together fails the load

The load SHALL fail when an event's battle set is missing or has other than one battle, a wave has no army and is not a clone wave, a clone wave has no health, a disallowed faction is not served, a ladder, chest or offer named by the event is missing, or a reward cannot be parsed. Validation SHALL report `InvalidSurvivalEvent` when the featured hero is not a served character, an enemy, summon or kill score names an npc that is not served, or no faction is allowed; and `UnknownSurvivalEvent` or `DuplicateSurvivalOccurrence` when a `survival` occurrence names an unserved id or dates an event a second time.

#### Scenario: A calendar typo

- **WHEN** a `survival` occurrence has `survivalEventId` `june-2026` and no such record exists
- **THEN** the catalog fails to load with `UnknownSurvivalEvent`
