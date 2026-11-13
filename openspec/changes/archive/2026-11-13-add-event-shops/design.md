## Context

See proposal.md for motivation. Current state:

- `ShopsDenormalizer` binds four daily merchants from `gameconfig/shop/merchants.json` and projects them into `GameCatalogShopView` (slots → variants with parsed reward/cost/cap/days, shard `unitId`, opaque `lockId`). Its doc comment states that limited-time event merchants are deliberately not bound.
- The pinned datamine (`1.43.157.2989`) already holds every event: merchants `June2026Week{1..3}EventShop`, `August2026Week{1,2}EventShop`, `October2026EventShop` (`displayLocation` `seasonalEventMerchant` / `October2026Merchant`, `liveEventRequired: true`); ladders `se_milestones_<month>_2026` and `se_progress_chests_<month>_2026[_weekN]` in `loot/tieredProgressRewards.json`, whose `chestRewardId`s point at `loot/chests.json` keys or are direct reward strings; mission quests in `quests/groups.json` groups `seasonal_daily` / `seasonal_seasonal` / `seasonal_seasonal_premium`, named `<prefix>_daily_NN`, `<prefix>_free_<n>_chain_NN`, `<prefix>_premium_chain_NN`.
- The datamine links these only by naming convention and carries no dates; V1 hard-codes start date, week list and income in `shop-events.ts` (`EVENT_META`) plus offline pre-processed JSON.
- The curated events calendar already has a `ShopEvent` definition (`anniversary-event`, required `weekNumber`) with two August 2026 week occurrences starting 2026-08-02 (V1 says 2026-08-05; the calendar date is used). June and October 2026 have no calendar entries yet.
- Per-profile plan persistence precedent: `LegendaryEventPlan` — one row per (profile, catalog id), jsonb children, `Revision` checked by a writer, unified error contract with a `…Stale` issue code.

## Goals / Non-Goals

**Goals:**
- One canonical served `event-shops` structure, derived from the datamine by convention and dated by the existing calendar — no new hand-kept file, one place per date.
- Reuse the daily shop variant projection unchanged so the client has one resolver.
- A small, whole-cart-replace plan API that is cheap to call on every cart edit.

**Non-Goals:**
- Discovering event dates from anything other than the curated calendar (the datamine has none).
- Importing V1 shop-event carts (V1 `shopEvents` state) — events are short-lived and V1 keys (`armageddon`, `august2026`, `october2026`) don't map 1:1 to the derived ids (`june-2026`, …) without a lookup; can be added to `V1Import` later.
- Battle-pass mission chains (V1 `missions.battlePass`) and live player progress (no sync source for event currency, progress or mission completion).
- Server-side lock resolution, purchase-cap tracking per real day, or currency budgeting.

## Decisions

### D1. Separate `event-shops` dataset, shared variant projection
Event shops have per-week slot sets, ladders, missions and a calendar anchor; the daily `shops` record has none of these. Adding them to `shops` would make every daily-shop consumer filter out events. So `event-shops` is its own dataset, but its slots are built by the same `BuildVariant` path (extracted to a shared helper) and validated by the same variant rules. *Alternative:* `shops` with `kind: "daily" | "event"` — rejected for the consumer-filtering cost and because daily-shop records would need nullable event fields.

### D2. Discovery by convention, dates from the calendar
Everything except dates is derivable: event merchants are `liveEventRequired` and named `<Month><Year>[Week<N>]EventShop`; currency, ladders and mission prefixes follow `<month>_<year>[_week<N>]` (rules in `specs/game-event-shops-dataset`). Dates and week count come from `ShopEvent` occurrences, which gain a required `eventKey` and optional `incomeFree` / `incomeBonusShipment`:

```jsonc
{ "id": "occ-2026-10-26-armageddon-shop-week1", "definitionId": "armageddon-shop",
  "startUtc": "2026-10-26T00:00:00Z", "endUtc": "2026-11-02T00:00:00Z",
  "parameters": { "weekNumber": 1, "eventKey": "October2026" } }
```

Seed data: new definition `armageddon-shop` (`ShopEvent`, recurrence `None`, required `weekNumber`, `eventKey`); `anniversary-event` gains required `eventKey`. Occurrences: `armageddon-shop` `June2026` weeks 1–3 from 2026-06-22 (income 1,000 / 900 each, from V1); existing `anniversary-event` weeks 1–2 get `eventKey` `August2026` and income 750 / 1,000; `armageddon-shop` `October2026` week 1 from 2026-10-26 (no income). The event's `definitionId` is served so the client names it from the calendar's existing i18n.

*Alternatives:* a separate curated `event-shops.json` (rejected — duplicates calendar dates, which already disagree with V1 for August); dates on the event-shop side with the calendar reading them (rejected — the calendar is the established home for dated events and their validation, and HSE already ties dataset behaviour to calendar parameters via `hseVariant`).

A calendar `eventKey` without a matching merchant skips the event instead of failing startup, so an older datamine-dropped event never breaks loading; a CI test (like the stale-HSE-calendar check) fails only for occurrences that have not ended.

### D3. Denormalization pipeline
`EventShopsDenormalizer.BuildEventShops(merchants, calendarOccurrences, ladders, chests, questGroups)`:
1. Group `ShopEvent` occurrences by `eventKey`, order by `weekNumber`; for each key with matching merchants, pick each week's merchant (`Week<N>` else shared), bind it as `GameCatalogRawShop` (same raw type as daily shops) → slots via the shared variant builder; read the week's optional income parameters.
2. Ladders → tiers: chest key lookup else reward-string parse (new `ShopNormalization.TryParseRewardRange`, accepting `type`, `type:qty`, `type:min-max`).
3. Missions by prefix: filter the three quest groups, group free chains by `<n>`, order by walking `successorQuests` from the quest that is no other quest's successor.
4. `dayCount = 7 × weeks`. Sort by `startUtc`.
Validation lives in `Validation/EventShopsValidation.cs`, reusing the variant checks from `ShopsValidation`, and fails startup with the event key in the message. The calendar's parameter validation (`eventKey` format, income values) extends the existing occurrence validation. Because the denormalizer reads the curated occurrences, it runs after they are loaded; it reads the authored occurrences, not the projected `events-calendar`. All four raw files are already read by existing denormalizers (`merchants.json` by `ShopsDenormalizer`, `tieredProgressRewards.json` by `EventDatamineReader`, `chests.json` by `LreDatamineReader`/`HreDenormalizer`, `groups.json` by `MythicQuestDenormalizer`); reuse their path constants and parsed documents rather than reading them twice.

### D4. Manifest
New dataset key `event-shops` in `GameCatalogDatasets`; it is hashed and listed like the others. `GameCatalogSnapshotTests` gains the new entry, and the `event-definitions` / `events-calendar` hashes change from the seeded calendar data; both snapshot updates are intentional.

### D5. Plan persistence: one row, jsonb entries
`EventShopPlan : BaseEntity<EventShopPlanId>, IRevisionedEntity` with `ProfileId`, `EventId`, `CatalogVersion`, `Revision`, `Entries` (jsonb list of `EventShopCartEntry`). Unique index (`ProfileId`, `EventId`), FK to profile with cascade delete. Migration `AddEventShopPlans` creates the table; no backfill. Whole-set replace keeps the API to one write endpoint; the cart is at most ~450 entries (3 weeks × 7 days × 21 slots) so payloads stay small. *Alternative:* relational `PLANNING_ARMAGEDDON_ITEMS` as sketched in `tacticus-planner-docs/architecture/data/player-and-planning.md` — rejected for now; no consumer queries entries independently, and the LRE plan already moved its children to jsonb for the same reason. The docs page gets a note.

### D6. Write validation against the catalog
`EventShopPlanCatalogValidator` resolves the event's week → slot → variants available on `day` and checks `rewardType` / `quantity` cap, ignoring `lockId` and power-level bounds (client-resolved, and roster-dependent). Equality for the no-op rule compares entries as a set keyed by (`week`, `day`, `slotIndex`).

### D7. Contract surface shared with apps
- Dataset: `GET /api/v1/game-catalog/event-shops` (shape in `specs/game-event-shops-dataset`).
- Endpoints: `GET /event-shop-plans`, `GET|PUT|DELETE /event-shop-plans/{eventId}`; issue code `eventShopPlanStale`. OpenAPI regenerates under `artifacts/openapi`; the apps change regenerates its typed client from it.

## Risks / Trade-offs

- [A future datamine drops an old event's merchant] → the event is no longer served; the calendar entry stays. Stored plans stay readable; only writes need the event.
- [A new event ships without calendar weeks] → it is silently not served. Mitigation: document in this repo's `game-catalog-data` skill that a datamine bump adds the new event's `ShopEvent` occurrences; the bump checklist lists new `*EventShop` merchants with no calendar `eventKey`.
- [A calendar date or week count is wrong] → the client's day mapping is off by days. Mitigation: dates live in one place and are reviewed with the calendar; validation rejects gaps, overlaps and non-7-day weeks. August uses the calendar's 2026-08-02 over V1's 2026-08-05.
- [Naming conventions change] → a dated event with an unmatched mission prefix fails the build, and a per-week event with a missing week merchant fails, so convention drift surfaces in the bump PR instead of serving empty data.
- [Slot indices shift if a merchant is edited mid-event] → a stored entry may fail the next write validation; the client drops entries that no longer resolve before writing (apps design), so the user loses that line instead of being stuck.

## Migration Plan

Additive only: new dataset, new table, new endpoints. Deploy API first (migration runs on startup), then the apps change. Rollback = revert; the table can be dropped by the reverse migration with no other dependency.

## Open Questions

- October 2026's real week count: the datamine has a single merchant and one milestone/progress ladder; seed one calendar week as V1 does and add occurrences when the event length is confirmed. This only changes data, not shape or tasks.
