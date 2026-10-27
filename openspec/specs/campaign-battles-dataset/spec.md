## Purpose

Defines the backend game catalog's served `campaign-battles` dataset fields that describe the allies of each campaign group, so a client can filter and display campaign battles by ally traits using ids only, without parsing the raw datamine.

## Requirements

### Requirement: Campaign battles serve the allies of their campaign group

Every battle in the served `campaign-battles` dataset SHALL carry `alliesAlliance` (string, one `Alliance` value) and `alliesFactions` (string array of faction ids, non-empty). The values are read once per campaign group from the game and copied onto every battle of that group, so all battles of a group have identical allies. The server SHALL send ids only.

`alliesFactions` SHALL be the factions the group's campaigns allow (`unlockConditions.requiredUnits.allowedFactions`), in the catalog's faction order; `alliesAlliance` SHALL be the alliance of the group's faction. Storyline Standard/Elite groups of a campaign share their allies; mirror groups ally with the side the standard campaign opposes; each event group carries its own pair of allied factions.

#### Scenario: Storyline battle allies

- **WHEN** `campaign-battles` is served
- **THEN** every battle in `campaign3` and `elite3` has `alliesAlliance` `Xenos` and `alliesFactions` `["Orks"]`, and every battle in `mirror3` and `eliteMirror3` has `alliesAlliance` `Imperial` and all Imperial faction ids

#### Scenario: Event battle allies

- **WHEN** `campaign-battles` is served
- **THEN** every `Standard` and `Extremis` battle (challenge or not) in `eventCampaign4` has `alliesAlliance` `Imperial` and `alliesFactions` `["BlackTemplars", "Sisterhood"]`

### Requirement: Allies are validated at catalog load

Catalog load SHALL fail when a campaign group has no `alliesAlliance`, an unknown alliance value, an empty `alliesFactions`, or an `alliesFactions` id that is not a known faction.

#### Scenario: Unknown ally faction

- **GIVEN** a campaign group whose `alliesFactions` contains an id with no faction
- **WHEN** the catalog loads
- **THEN** validation throws naming the group and the unknown id

### Requirement: Campaign battles serve their lightning victory

Every battle of the served `campaign-battles` dataset SHALL carry `lightningVictoryTurns` (int or null): the battle's `lightningVictory` in `battles/campaigns.json`, null when the battle has none. Its `energyCost` remains the game's `staminaCost`.

#### Scenario: Indomitus lightning victories

- **WHEN** `campaign-battles` is served from datamine `v1.43.157.2989`
- **THEN** `campaign1` nodes 1 to 18 have `lightningVictoryTurns` null and node 19 has a number

### Requirement: Campaign definitions serve their campaign number

Every record of the served `campaign-definitions` dataset SHALL carry `number` (int): the trailing number of its first campaign's id (`campaign{n}`, `mirror{n}`, `elite{n}`, `eliteMirror{n}` or `eventStandard{n}`), so the four storyline groups of one campaign share a number and `eventCampaign{n}` has `n`. Catalog load SHALL fail when a storyline battle's `campaignId` differs from its group's number, or a storyline campaign id has no trailing number.

#### Scenario: Campaign numbers

- **WHEN** `campaign-definitions` is served
- **THEN** `campaign1` has `number` 1, `mirror2` 2, `elite4` and `eliteMirror4` 4, and `eventCampaign6` 6

### Requirement: Served campaign battles carry their Tacticus battle index

Every battle in the served campaign-battles dataset SHALL carry `battleIndex`: the zero-based index Tacticus's own campaign-progress payloads use to identify that battle, assigned independently within each `{campaignGroupId, type}` track in that track's canonical order. This lets a consumer resolve a synced battle-attempt record (`campaignId`, `type`, `battleIndex`) to a specific served battle for any campaign, including an event campaign whose challenge nodes share their preceding node's `nodeNumber` and so cannot be distinguished by `nodeNumber` alone.

#### Scenario: A standing campaign's battles index sequentially

- **GIVEN** a standing campaign group's battles, ordered by node number, with no challenge nodes
- **WHEN** the campaign-battles dataset is served
- **THEN** each battle's `battleIndex` equals its position in that order, starting at 0

#### Scenario: An event campaign's challenge node has its own battle index despite sharing a node number

- **GIVEN** an event campaign's Standard track where node 3's challenge battle is interleaved after node 3's regular battle, both carrying `nodeNumber` 3
- **WHEN** the campaign-battles dataset is served
- **THEN** the regular node-3 battle and its challenge battle carry two different, sequential `battleIndex` values within that track — not the same value and not derived from `nodeNumber`

#### Scenario: Each event tier indexes independently

- **GIVEN** an event campaign group with a Standard track and an Extremis track sharing one campaign group id
- **WHEN** the campaign-battles dataset is served
- **THEN** the Standard track's battles and the Extremis track's battles are each indexed starting at 0 within their own `type`, independently of the other track
