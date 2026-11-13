## Why

Seasonal event shops (Armageddon Shop, War Amongst the Stars, ...) are the 4th most used V1 workflow — 31.9% of wave-1 survey respondents, 46.2% of heavy/power users — and V2 has nothing for them yet (tacticus-planner-apps#24). The V1 page runs on hand-copied JSON and hand-written event metadata per event; V2 can serve the same data from the pinned datamine, which already carries every event merchant, milestone ladder, progress-chest ladder and mission chain, and can persist the player's cart and purchases server-side.

This is the backend half of the paired change `add-event-shops`; the companion `tacticus-planner-apps` change of the same name builds the event shop page, cart, purchase tracking, coverage and the reworked shopping list (tacticus-planner-apps#508). Apply this half first.

## What Changes

- New served catalog dataset `event-shops`: one record per seasonal event, with its start instant, its weeks (each with the shop slots and variants for that week, its progress-chest ladder, its mission chains and optional estimated income), its milestone ladder, and its currency id. Events, merchants, ladders, missions and currency are discovered from the pinned datamine by naming convention; dates and week count come from the existing curated events calendar (`ShopEvent` occurrences). No new hand-kept file.
- Events calendar: `ShopEvent` occurrences gain a required `eventKey` and optional `incomeFree` / `incomeBonusShipment` parameters; a new `armageddon-shop` definition and occurrences for the June and October 2026 Armageddon shops are added.
- Event shop variants reuse the daily `shops` variant projection (structured reward/free offer, cost, cap, explicit days, power-level bounds, opaque lock id, shard `unitId`), so the client resolves both with one resolver.
- New per-profile **event shop plan**: one plan per (profile, event id) holding the cart (week, day, slot, chosen reward type, quantity, purchased quantity, draft alliance) behind a revision, with read, list, replace-entries and delete endpoints.
- EF Core migration adding the event shop plan table.
- The `shops` dataset requirement that it is "the only shop dataset exposed publicly" is relaxed so `event-shops` can be served beside it.

## Capabilities

### New Capabilities
- `game-event-shops-dataset`: the served `event-shops` catalog dataset — datamine merchants, milestone and progress-chest ladders and mission chains discovered by convention, dated by the events calendar.
- `event-shop-plans`: per-profile cart and purchase tracking for one event shop, with revision-guarded writes validated against the current catalog.

### Modified Capabilities
- `game-shops-dataset`: "The catalog serves one consolidated `shops` dataset" no longer claims `shops` is the only shop dataset; event shops are served separately as `event-shops`.
- `game-events-calendar-dataset`: `ShopEvent` occurrences require `eventKey` and may carry income parameters; the calendar gains `armageddon-shop` and the event-shop weeks currently in the datamine.

## Impact

- `src/TacticusPlanner.GameCatalog`: curated `event-definitions.json` / `event-occurrences.json` (new definition, `eventKey` on shop-event occurrences, June/October weeks), raw bindings for event merchants / `loot/tieredProgressRewards.json` / `loot/chests.json` / `quests/groups.json`, a new denormalizer and validator, the public manifest (new dataset key) and its snapshot test. The served `event-definitions` / `events-calendar` hashes change.
- `src/TacticusPlanner.Domain` / `Persistence`: new `EventShopPlan` entity, configuration and migration.
- `src/TacticusPlanner.Api`: new `Features/EventShopPlans` endpoints; regenerated `artifacts/openapi`; account deletion and staging transfer include the new plans.
- Companion: `tacticus-planner-apps` change `add-event-shops` consumes `event-shops` and the `/event-shop-plans` endpoints.
