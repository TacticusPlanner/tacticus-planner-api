## Purpose

Defines the backend game catalog's served `campaign-battles` dataset fields that describe the allies of each campaign group, so a client can filter and display campaign battles by ally traits using ids only, without parsing the raw datamine.

## Requirements

### Requirement: Campaign battles serve the allies of their campaign group

Every battle in the served `campaign-battles` dataset SHALL carry `alliesAlliance` (string, one `Alliance` value) and `alliesFactions` (string array of faction ids, non-empty). The values are authored once per campaign group in the raw `campaign-battles-*.json` (fields `alliesAlliance`, `alliesFactions`) and copied by denormalization onto every battle of that group, so all battles of a group, regardless of `type` or `challenge`, have identical allies. The server SHALL send ids only; names, icons and labels are client-derived. `campaign-definitions` SHALL NOT change.

Authored values per group follow V1's `getEnemiesAndAllies`: storyline Standard/Elite groups of a campaign share their allies; mirror groups ally with the side the standard campaign opposes; each event group carries its own pair of allied factions.

#### Scenario: Storyline battle allies

- **WHEN** `campaign-battles` is served
- **THEN** every battle in `campaign3` and `elite3` has `alliesAlliance` `Xenos` and `alliesFactions` `["Orks"]`, and every battle in `mirror3` and `eliteMirror3` has `alliesAlliance` `Imperial` and all Imperial faction ids

#### Scenario: Event battle allies

- **WHEN** `campaign-battles` is served
- **THEN** every `Standard` and `Extremis` battle (challenge or not) in `eventCampaign4` has `alliesAlliance` `Imperial` and `alliesFactions` `["Sisterhood", "BlackTemplars"]`

### Requirement: Allies are validated at catalog load

Catalog load SHALL fail when a campaign group has no `alliesAlliance`, an unknown alliance value, an empty `alliesFactions`, or an `alliesFactions` id that is not a known faction.

#### Scenario: Unknown ally faction

- **GIVEN** a campaign group whose `alliesFactions` contains an id with no faction
- **WHEN** the catalog loads
- **THEN** validation throws naming the group and the unknown id

### Requirement: Adding allies does not break the served contract

The change SHALL NOT bump `SchemaVersion`. Among datasets, the manifest SHALL show a changed hash for `campaign-battles` only (plus `sourceHash`), and `campaign-definitions` SHALL be unchanged.

#### Scenario: Manifest after the change

- **WHEN** the manifest snapshot is compared to the previous release
- **THEN** only the `campaign-battles` hash and `sourceHash` differ
