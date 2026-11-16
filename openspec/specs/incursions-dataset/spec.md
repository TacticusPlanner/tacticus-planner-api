# incursions-dataset Specification

## Purpose

Describes the served `incursions` dataset: each Machine of War Incursion with its run tokens, enhancements, tiers,
battles and run map, built from the pinned datamine.

## Requirements

### Requirement: Incursions are built from the pinned datamine files

The served `incursions` dataset SHALL hold one record per MoW of the single event in the datamine copy
`gameconfig/liveEvents/mowEvent.json`, ordered by `availableFromUtc`, then `mowId`, with each run-map chest's type and
rewards from `gameconfig/loot/chests.json`.

#### Scenario: The shipped Incursions

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds 11 MoWs, the first `astraOrdnanceBattery` and the last `necroReanimator`, each with 14 tiers from
  Common to Mythic

### Requirement: Each Incursion carries the event rules

Each record SHALL carry `availableFromUtc` (`yyyy-MM-ddTHH:mm:ssZ`), the game's `new` and `updated` flags,
`allowedFactions`, `tokens` (`start`, `max`, `regenSeconds`, `regenAmount`, `costPerRun`),
`minUnitsOfGrandAllianceNeeded`, `ohSoCloseOffer` and the `enhancements` its run maps use, keyed by enhancement id.

#### Scenario: The Broadside's event rules

- **WHEN** the dataset is served
- **THEN** the `tauBroadside` record has `availableFromUtc` `2024-12-02T08:00:00Z`, 10 allowed factions, tokens starting
  at 2 with a max of 3, 1 token regenerated every 72001 seconds and 1 spent per run, needs 3 Grand Alliance units, and
  its `enhancements` are exactly the ones its run-map nodes name, including `Heal_01`

### Requirement: Each tier carries its battles and run map

Each tier SHALL carry `index`, `rarity`, `battles` (`id`, `boardId`, `challenge`, `difficultyMultiplier`,
`lightningVictoryTurns`, `baseRewards`, `starRewards` for 1 to 3 stars, `enemies` grouped by `npcId` and `level` with
a `count`) and `nodes` (`index`, `depth`, `kind` of `battle`, `enhancement` or `chest`, `name`, the matching
`battleId`, `enhancementId` or `chest`, `unlockedBy` and `exclusiveWith` node indices). Rewards are the game's
`type[:amount]` strings.

#### Scenario: The Broadside's first battle

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** tier 0 battle `1` is on board `EC3_11` and pays `shards_tauBroadside:2`, `upgArmC005:2` and `upgDmgC015:3`

### Requirement: Unresolvable data fails the load

The load SHALL fail when a node names an unknown battle, enhancement or chest, a node type is unknown, a node's
`unlockedBy` or `exclusiveWith` is out of range, an enemy is not `npcId:level`, or a battle lacks three star rewards.
Validation SHALL report `InvalidIncursion` when the MoW, an allowed faction, a tier rarity or an enemy npc is not served.

#### Scenario: A run-map node names an unknown chest

- **WHEN** a node names chest `mowe_x_00_01` and `chests.json` has no such chest
- **THEN** the load fails with a message that the chest is missing

#### Scenario: Unserved references fail validation

- **WHEN** an Incursion names an unserved MoW, the faction `Squats`, the rarity `Shiny` or an unknown enemy npc
- **THEN** validation reports an `InvalidIncursion` error for each
