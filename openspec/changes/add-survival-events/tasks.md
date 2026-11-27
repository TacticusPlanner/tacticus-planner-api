## 1. Survival dataset

- [x] 1.1 Add `liveEvents/seasonalEvent.json`, `shop/offers.json` and `shop/realMoneyProducts.json` to `Data/datamine/files.txt` and pull them at the pinned tag.
- [x] 1.2 Add the `SurvivalEvents` models, `SurvivalEventsDenormalizer` (reusing the event shop ladder and mission readers) and `SurvivalEventsValidation`, register `survival-events` in `GameCatalogDatasets.Served`, and verify denormalizer tests for ids, dates, rules, waves, clone waves, scoring, ladders, chests, missions and offers.
- [x] 1.3 Add the `survival-events` endpoint, and verify an endpoint test and the promoted manifest snapshot.

## 2. Calendar

- [x] 2.1 Add the `survival` definition and the May, September and December 2026 occurrences, and verify a validation test for an unknown and a twice-dated event.

## 3. Teams

- [x] 3.1 Add `SurvivalEventKey` to `Team`, `TeamDraft`, the contracts and the staging transfer, `TeamSurvivalFacts`, the five rule codes, and migration `AddTeamSurvivalEventKey`; verify unit tests for each rule and an integration test for a round trip and a refused save.
- [x] 3.2 Import V1 `survivalTeams`, and verify an integration test for an imported, a skipped and an empty event.
- [x] 3.3 Verify the regenerated `artifacts/openapi` and hand the contract to the apps change.
