## 1. Catalog: calendar data and models

- [x] 1.1 In `Data/curated/event-definitions.json` add `armageddon-shop` (`ShopEvent`, recurrence `None`, required `weekNumber`, `eventKey`) and add `eventKey` to `anniversary-event`'s required parameters; in `event-occurrences.json` add `armageddon-shop` occurrences for `June2026` weeks 1–3 (2026-06-22, 06-29, 07-06; `incomeFree` 1000, `incomeBonusShipment` 900) and `October2026` week 1 (2026-10-26), and add `eventKey` `August2026`, `incomeFree` 750, `incomeBonusShipment` 1000 to the two existing `anniversary-event` weeks; verify the existing calendar tests pass with the new entries
- [x] 1.2 Extend occurrence validation: `eventKey` format, non-negative integer `incomeFree` / `incomeBonusShipment` on `ShopEvent` occurrences only; verify with tests for a missing `eventKey`, a malformed one and a negative income
- [x] 1.3 Add served model records (`GameCatalogEventShopView` with `eventKey`, `definitionId`, week with optional `EstimatedWeeklyIncome`, tier, `RewardRange`, mission, chain, task) with `JsonIgnore(WhenWritingNull)` on optional fields; verify serialization matches the spec shape in a model test
- [x] 1.4 Extract the daily-shop variant builder and variant validation into shared helpers used by both `ShopsDenormalizer` and the new denormalizer; verify existing `shops` tests still pass unchanged

## 2. Catalog: denormalization and validation

- [x] 2.1 Add `ShopNormalization.TryParseRewardRange` (`type`, `type:qty`, `type:min-max`) with tests for each form and for rejects
- [x] 2.2 Implement `EventShopsDenormalizer` (group `ShopEvent` occurrences by `eventKey`, per-week vs shared merchant, derived currency / ladder keys / mission prefixes, ladder tiers with chest lookup, missions with successor ordering, per-week income, `dayCount`, `definitionId`, sort by `startUtc`); verify with tests over the pinned datamine: `june-2026` week 2 sources, `october-2026` shared sources, the October Yarrick variant, a chest-backed and a direct milestone tier, the 20-mission `october_2026_free_1` chain in order, a task with `parameters`, August income on both weeks, an undated discovered merchant not served
- [x] 2.3 Implement `EventShopsValidation` for every failure in the spec (missing week merchant, week numbers not 1..W, non-7-day or non-midnight week, gap/overlap, duplicate week, unmatched mission prefix, unparseable reward range, variant rule failures, currency mismatch) and the skip rule (calendar `eventKey` with no merchant is not served and does not fail); verify one test per failure asserting the event key appears in the message, and one for the skip
- [x] 2.4 Add a CI test (alongside the stale-HSE-calendar test) that fails, naming the event key, when a `ShopEvent` occurrence that has not ended has no matching event merchant; verify it passes on the seeded data and fails on a fixture with a future unmatched `eventKey`
- [x] 2.5 Register `event-shops` in `GameCatalogDatasets` and the loader; verify `GET /api/v1/game-catalog/event-shops` returns the three events in start order in an API integration test
- [x] 2.6 Update the `game-shops-dataset` coverage with a test that `shops` still has exactly four records with event merchants present
- [x] 2.7 Update `GameCatalogSnapshotTests` and `TacticusPlanner.GameCatalog.Tests` denormalization coverage for the new manifest entry; verify the snapshot diff only adds `event-shops` and changes the `event-definitions` / `events-calendar` hashes

## 3. Event shop plans: persistence

- [x] 3.1 Add `EventShopPlan` entity (`EventShopPlanId`, `ProfileId`, `EventId`, `CatalogVersion`, `Revision`, jsonb `Entries`) and EF configuration with unique (`ProfileId`, `EventId`) and cascade delete from profile; verify in a persistence integration test
- [x] 3.2 Run `dotnet ef migrations add AddEventShopPlans` and verify the migration creates only the new table and index; confirm `api-migrations` applies it on a fresh database

## 4. Event shop plans: API

- [x] 4.1 Add contracts (`EventShopPlanResponse`, `EventShopPlanListResponse`, `PutEventShopPlanRequest`, entry DTO) and `EventShopPlanCatalogValidator` covering every 400 rule in the spec; verify with unit tests per rule (cap, day availability, purchased > quantity, draft alliance rules, duplicates, >1000 entries, unknown event → 404)
- [x] 4.2 Implement `GET /event-shop-plans`, `GET|PUT|DELETE /event-shop-plans/{eventId}` with the revision rule (409 `eventShopPlanStale` with `current`, no-op PUT keeps revision, concurrent create) and response ordering by event day order; verify with API integration tests including another profile's isolation and a plan whose event left the catalog
- [x] 4.3 Include event shop plans in account deletion and in staging transfer export/import; verify with the existing account-deletion and staging-transfer integration tests extended with plans
- [x] 4.4 Build and verify the regenerated `artifacts/openapi` contains the new dataset model and endpoints; note the contract in the companion apps change `add-event-shops`

## 5. Docs and gates

- [x] 5.1 Update `.agents/skills/game-catalog-data` (and its `.claude` mirror per repo convention) with the event-shop naming conventions, how to add a new event's `ShopEvent` calendar weeks (`eventKey`, optional income) during a datamine bump, and what happens when a datamine drops an old event merchant
- [x] 5.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore` and `dotnet test TacticusPlanner.slnx -c Release --no-build`; all pass
- [x] 5.3 Start the stack through Aspire, wait for `api` and `api-migrations` to be healthy, and verify `GET /api/v1/game-catalog/event-shops` and a PUT/GET round-trip of an event shop plan as a signed-in user

## Deferred / out-of-session

- [ ] D.1 Note in `tacticus-planner-docs/architecture/data/player-and-planning.md` that Armageddon/event shop plans are stored as one jsonb row per event (`EventShopPlan`) rather than the sketched relational tables — docs repo, cross-repo PR (tracking: tacticus-planner-apps#24)
