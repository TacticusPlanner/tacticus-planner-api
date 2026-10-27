# Tasks

Companion apps change: roadmap step 7 apps PRs (after all step 7 API PRs). Source plan:
`tacticus-planner-data-mines` `docs/roadmap.md` step 7.

## 1. Binding

- [x] 1.1 Bind the stamina, run and offer fields of `legendaryHeroEvent.json` and serve `tokens`, `maxRuns`, `ohSoCloseOffer` on `lres`
- [x] 1.2 Bind the stamina fields and `scorePoints` of `linearHeroEvent.json` and serve `tokens`, `scorePoints` on `quest-events`
- [x] 1.3 Bind `lightningVictory` and `campaignId` of `campaigns.json`; serve `lightningVictoryTurns` on `campaign-battles` and `number` on `campaign-definitions`
- [x] 1.4 Share the Oh So Close record with `hre`; bump `SchemaVersion` to 15

## 2. Validation

- [x] 2.1 Fail startup on missing or out-of-range token, run, offer and score values
- [x] 2.2 Fail startup when a storyline battle's `campaignId` differs from its group's number

## 3. Verification

- [x] 3.1 Reader tests for each new field, its failures and the shipped values
- [x] 3.2 Manifest snapshot: `schemaVersion` 15, only the four extended datasets' hashes move
- [x] 3.3 Format, build, all test projects

## 4. Docs

- [x] 4.1 Update the `game-catalog-data` skill (both mirrors)
