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

### Requirement: A campaign definition names the campaign it follows

A storyline campaign definition SHALL carry `requiredCampaignGroupId`: the id of the group that must be fully cleared before it opens, read from its first campaign's `completedCampaigns` unlock condition (a storyline group's id is its campaign id). It SHALL be null when the campaign follows none, and for campaign-event groups, whose Extremis track follows the group's own Standard track. A storyline campaign that follows a campaign the game does not have, an event campaign, or more than one campaign SHALL fail the load; event groups are not checked.

#### Scenario: Elite and Mirror Elite campaigns

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** `elite1`-`elite4` follow `campaign1`-`campaign4`, `eliteMirror1`-`eliteMirror4` follow `mirror1`-`mirror4`, and every other group follows none

### Requirement: Battle enemies come from the opposing teams

A battle's enemies SHALL be the `npcId:index` units of every team whose `playerTeams` value is not 1, without board power-ups, counted per unit and sorted by npc id, then index. `stars` and `rank` SHALL come from the npc's stat row at the 1-based index, for bosses and every other npc, and `servedIndex` SHALL be that row's 0-based position. An index past the npc's ladder SHALL fail the load.

#### Scenario: Allied team

- **GIVEN** a battle whose second team has `playerTeams` 1
- **WHEN** the catalog loads
- **THEN** that team's units are not served as enemies or counted in `enemiesTotal`

#### Scenario: A regular npc's index is 1-based

- **GIVEN** a battle enemy `admecNpc1Vanguard:1`
- **WHEN** the catalog loads
- **THEN** its `stars` and `rank` come from that npc's first stat row and its `servedIndex` is 0

### Requirement: Drop chance ids are derived from the reward

A battle's potential reward SHALL be its `chanceOf` reward with `chanceId` `{kind}_{difficulty}`: kind `shard`, `mythicShard` or `upgrade{Rarity}`, difficulty `standard`, `mirror`, `elite` (Elite and EliteMirror) or `eventStandard`/`eventExtremis`, with `Challenge` appended for an event challenge battle. A drop that is neither a shard nor a known upgrade SHALL fail the load.

#### Scenario: Event challenge drop

- **GIVEN** an event Standard challenge battle that drops an Epic upgrade
- **WHEN** the catalog loads
- **THEN** its potential reward has `chanceId` `upgradeEpic_eventStandardChallenge`

### Requirement: One-time medal and milestone rewards are read from the datamine

A battle's `rewards.medals` SHALL be its `loot.star1`-`star3` rewards (`medals` 1-3, `id[:min[-max]]` read as a guaranteed reward), leaving out a star with no reward. An event battle's `rewards.milestones` SHALL be its `bronzeMedal`/`silverMedal`/`goldMedal` loot (`milestone` `Bronze`, `Silver`, `Gold`) from `liveEvents/campaignEvent.json`, matched by campaign id and `campaignBattleId`. Loading SHALL fail when an event battle has no entry there; non-event battles have no milestones.

#### Scenario: Extremis milestones

- **WHEN** the catalog loads from datamine `v1.43.157.2989`
- **THEN** every `eventExtremis{n}` battle has a single `Gold` milestone and every `eventStandard{n}` battle has `Bronze`, `Silver` and `Gold`
