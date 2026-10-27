# Proposal

## Why

`tacticus-planner-data-mines` `docs/roadmap.md` step 7 moves game constants the apps still hardcode into the catalog.
Four already-served datasets lack fields the game ships in files the catalog already reads:

- LRE token rules (`token-supply.ts`, `run-forecast.ts`, `lifecycle.ts`: start 6, max 12, regen 3 h, 3 runs, Oh So
  Close pricing) are `startStamina`, `maxStamina`, `staminaRegenerationTime`, `heroEventMaxRuns` and `ohSoCloseOffer`
  in `liveEvents/legendaryHeroEvent.json`.
- Quest token rules (`quest-token-plan.ts`: bank 6, regen 2 h) and medal and lightning scoring are `maxStamina`,
  `staminaRegenerationTime` and `scorePoints` in `liveEvents/linearHeroEvent.json`.
- The first Indomitus node with a lightning victory (`FIRST_LIGHTNING_INDOMITUS_NODE = 19`) is each battle's
  `lightningVictory` in `battles/campaigns.json`.
- Campaign numbering (`icons/campaign.ts`) is the trailing number of the campaign ids.

## What Changes

- **`lres`** gains `tokens` (`start`, `max`, `regenSeconds`, `regenAmount`, `dailyRefreshes`), `maxRuns` and
  `ohSoCloseOffer` (the `hre` shape; the record is now shared and named `GameCatalogOhSoCloseOffer`).
- **`quest-events`** gains `tokens` (`max`, `regenSeconds`, `regenAmount`, `refillGemsCost`) and `scorePoints`
  (`pointsPerStar`, `maxPointsForLightningVictory`).
- **`campaign-battles`** gains `lightningVictoryTurns` (null when the battle has none). `energyCost` already serves the
  game's `staminaCost`.
- **`campaign-definitions`** gains `number`.
- `SchemaVersion` 14 → 15. The four hashes move; no other does. No new datamine file.
- **Validation.** Missing or out-of-range token, run or offer values fail startup; so does a storyline battle whose
  `campaignId` differs from its group's number.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `quest-events-dataset`: the served shape adds `tokens` and `scorePoints`.
- `legendary-event-datamine-source`: events serve the token bank, run count and Oh So Close offer.
- `legendary-event-run-schedule`: `maxRuns` is allowed beside `eventStageStartDatesUtc`.
- `campaign-battles-dataset`: battles serve `lightningVictoryTurns`, definitions `number`.

## Impact

- Companion apps change: roadmap step 7 apps PRs replace the constants above after all step 7 API PRs merge. Today's
  apps parse with loose objects and ignore the new fields.
- `src/TacticusPlanner.GameCatalog`: `Models/Lres.cs`, `Models/DatamineLres.cs`, `Models/QuestEvents.cs`,
  `Models/DatamineEvents.cs`, `Models/Campaigns.cs`, `Models/OhSoCloseOffer.cs` (new), `Models/Hre.cs`,
  `Models/GameCatalogRelease.cs`; `LreDatamineReader.cs`, `LreDenormalizer.cs`, `EventDatamineReader.cs`,
  `QuestEventsDenormalizer.cs`, `CampaignDatamineReader.cs`, `CampaignDenormalizer.cs`.
- `src/TacticusPlanner.Api`: OpenAPI artifact only.
- Tests: reader tests for each field; manifest snapshot `schemaVersion` 15 with four hashes moved.
- Docs: `game-catalog-data` skill (both mirrors).
- No EF Core migration, no infra change.
