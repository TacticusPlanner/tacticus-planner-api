# hre-dataset Specification

## Purpose

Describes the served `hre` dataset: each Hero Release Event with its hero, scoring, milestones, missions, leaderboard,
chest shop and Oh So Close offer, built from the pinned datamine.

## Requirements

### Requirement: Hero Release Events are built from the pinned datamine files

The served `hre` dataset SHALL hold one record per event of the datamine copy `gameconfig/liveEvents/heroReleaseEvent.json`,
ordered by id, with its missions from `gameconfig/quests/groups.json`, its leaderboard from `gameconfig/leaderboards.json`,
its chests from `gameconfig/loot/chests.json` and its chest shop from `gameconfig/shop/products.json`.

#### Scenario: The shipped events

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** it holds `hero_release_event_1` (orksWeirdboy, Rare to Legendary, Orks) and `hero_release_event_2`

### Requirement: Each event carries its milestones and missions

Each record SHALL carry `milestones` (each `scorePoints` and the `currency` it pays) and `missions` in chain order from
`questId` along `successorQuests`, each with `index` (from 1), `id`, `bonus` (true for the chain's last mission),
`currency`, `rewards` and `tasks` (`name`, `target`, `parameters`).

#### Scenario: The Weirdboy event

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** its 36 milestones pay 6090 currency in total and its 21 missions pay 2000, the last being the bonus

### Requirement: Each event carries its leaderboard and chest shop

Each record SHALL carry `leaderboard` (`id`, `dailyRewards` of `position` and `currency`, and `phases` of
`endOffsetDays` and `tiers` with `position`, `chestId`, `currency` and `shards`) and `chestShop` (`chestId`, `cost`,
`minShards`, `maxShards`, by chest id). Event N SHALL read `heroReleaseLeaderboard` for N = 1 and
`heroReleaseLeaderboard_N` otherwise. Positions are the bracket's first 0-based position.

#### Scenario: First place

- **WHEN** the dataset is served at datamine `v1.43.157.2989`
- **THEN** position 0 pays 100 currency a day and its week-one chest holds 550 currency and 33 shards, and the first
  shop chest costs 100 currency for 10-11 shards

### Requirement: Unmappable events fail startup

The catalog loader SHALL fail startup, naming `hre` and the event, when an event name is not `hero_release_event_N`,
it lacks a hero, featured faction, `questId` or Oh So Close offer, a reward is not the expected `type:N` form, the
mission chain breaks, or a quest group, leaderboard or chest is missing.

#### Scenario: A missing leaderboard

- **GIVEN** an event `hero_release_event_2` and no `heroReleaseLeaderboard_2`
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `hre` and the leaderboard

### Requirement: Event references resolve

Validation SHALL fail startup with an `InvalidHre` error when an event's hero or a mission task's `heroId` is not a
served character, its rarities are unknown, its featured faction or a task's `factionId` is not a served faction, a
task's `factionTrait` is not an alliance, or a task's `damageProfileType` is not a served damage type.

#### Scenario: An unknown task faction

- **GIVEN** a mission task with `factionId` `Squats`
- **WHEN** the catalog is validated
- **THEN** an `InvalidHre` error names the mission and `Squats`
