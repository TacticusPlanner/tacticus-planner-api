## MODIFIED Requirements

### Requirement: Campaign battles serve the allies of their campaign group

Every battle in the served `campaign-battles` dataset SHALL carry `alliesAlliance` (string, one `Alliance` value) and `alliesFactions` (string array of faction ids, non-empty). The values are curated once per campaign group in `Data/curated/campaign-curation.json` and copied onto every battle of that group, so all battles of a group have identical allies. The server SHALL send ids only. `campaign-definitions` SHALL NOT change.

Curated values follow V1's `getEnemiesAndAllies`: storyline Standard/Elite groups of a campaign share their allies; mirror groups ally with the side the standard campaign opposes; each event group carries its own pair of allied factions.

#### Scenario: Storyline battle allies

- **WHEN** `campaign-battles` is served
- **THEN** every battle in `campaign3` and `elite3` has `alliesAlliance` `Xenos` and `alliesFactions` `["Orks"]`, and every battle in `mirror3` and `eliteMirror3` has `alliesAlliance` `Imperial` and all Imperial faction ids

#### Scenario: Event battle allies

- **WHEN** `campaign-battles` is served
- **THEN** every `Standard` and `Extremis` battle (challenge or not) in `eventCampaign4` has `alliesAlliance` `Imperial` and `alliesFactions` `["Sisterhood", "BlackTemplars"]`
