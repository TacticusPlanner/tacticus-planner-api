# campaign-battles-datamine-source Specification

## Purpose
Defines how the backend game catalog builds its campaign battles from the pinned datamine: which campaign each group reads, how battle ids, enemies and drop chance ids are derived, and the group metadata kept by hand.

## Requirements

### Requirement: Campaign battles are read from the pinned datamine

Each campaign group SHALL be read from `Data/curated/campaign-curation.json` (its metadata and, per Tacticus type, a datamine campaign and battle-id prefix) and the battles of those campaigns in `battles/campaigns.json`, in game order. Loading SHALL fail when the curated keys differ from `CampaignBattleGroups` or a curated campaign is missing.

#### Scenario: Battle ids

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** `campaign1` battle `07` is served as `I07` and `eventStandard1` battle `13B` as the challenge battle `AMSC13B`

### Requirement: Battle enemies come from the opposing teams

A battle's enemies SHALL be the `npcId:index` units of every team whose `playerTeams` value is not 1, without board power-ups, counted per unit and sorted by npc id, then index. `stars` and `rank` SHALL come from the npc's stat row: a boss index is 1-based, any other npc's is 0-based and capped at the last row.

#### Scenario: Allied team

- **GIVEN** a battle whose second team has `playerTeams` 1
- **WHEN** the catalog loads
- **THEN** that team's units are not served as enemies or counted in `enemiesTotal`

### Requirement: Drop chance ids are derived from the reward

A battle's potential reward SHALL be its `chanceOf` reward with `chanceId` `{kind}_{difficulty}`: kind `shard`, `mythicShard` or `upgrade{Rarity}`, difficulty `standard`, `mirror`, `elite` (Elite and EliteMirror) or `eventStandard`/`eventExtremis`, with `Challenge` appended for an event challenge battle. A drop that is neither a shard nor a known upgrade SHALL fail the load.

#### Scenario: Event challenge drop

- **GIVEN** an event Standard challenge battle that drops an Epic upgrade
- **WHEN** the catalog loads
- **THEN** its potential reward has `chanceId` `upgradeEpic_eventStandardChallenge`
