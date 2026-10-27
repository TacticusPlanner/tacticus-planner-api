## ADDED Requirements

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
