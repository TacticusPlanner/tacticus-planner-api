# campaign-battles-datamine-source Specification

## Purpose
Defines how the backend game catalog builds its campaign battles from the pinned datamine: which campaign each group reads, how battle ids, enemies and drop chance ids are derived, and how group metadata follows the game's unlock conditions.

## Requirements

### Requirement: Campaign battles are read from the pinned datamine

Each campaign group SHALL be read from `Data/curated/campaign-curation.json` (its datamine campaigns, in order, each with V1's battle-id prefix) and the battles of those campaigns in `battles/campaigns.json`, in game order. Loading SHALL fail when the curated keys differ from `CampaignBattleGroups` or a curated campaign is missing.

#### Scenario: Battle ids

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** `campaign1` battle `07` is served as `I07` and `eventStandard1` battle `13B` as the challenge battle `AMSC13B`

### Requirement: Group ids and types follow the campaign ids

A campaign's type SHALL be its `campaigns.json` kind, except an event campaign, which is `Extremis` when its `completedCampaigns` names an event campaign and `Standard` otherwise. A group's id SHALL be its first campaign's id (`eventCampaign{n}` for `eventStandard{n}`), and its release type `event` for event campaigns and `standard` otherwise.

#### Scenario: Event group

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** the group reading `eventStandard1` and `eventExtremis1` has id `eventCampaign1`, release type `event` and types `["Standard", "Extremis"]`

### Requirement: Core characters and faction follow the unlock conditions

Core characters SHALL be the `unlockedUnitIds` of the group's campaigns, or of the campaign a campaign follows when it names none. They are curated only when the game names none (Indomitus), and curating them anyway SHALL fail the load. The group's faction SHALL be the one faction of its core characters. An allowed faction with no units SHALL fail the load.

#### Scenario: Elite campaign

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** `elite2` (which follows `campaign2`) has core characters `["blackHaarken", "blackTerminator", "blackPossession"]`, faction `BlackLegion`, and allies including `TheLostAndTheDamned`

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
