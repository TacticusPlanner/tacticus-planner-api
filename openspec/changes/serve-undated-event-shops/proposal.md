## Why

The datamine ships an event's shop (its merchants, ladders and missions) before anyone knows its start date: the game data has no timestamps, and the curated calendar only dates an event once its schedule is published. Today such an event is not served at all, so the planner hides a shop the game files already describe. Serving it without a date lets the client show it as "Start date TBA" and browse its contents early.

Companion apps change: `show-undated-event-shops` in `tacticus-planner-apps`. The apps half deploys first, since today's client rejects a record whose `startUtc` or `definitionId` is null.

## What Changes

- A discovered event with no calendar weeks is served undated: `startUtc` and `definitionId` are null, its weeks come from its merchants (week 1 for a shared merchant, else 1 up to the first missing `Week<N>` merchant) and carry no estimated income, and `dayCount` is `7 × weeks.length`.
- Only undated events whose month is not before the month the latest dated event starts in are served, so an old event the calendar never dated does not appear as upcoming.
- The dataset is ordered dated-by-start first, then undated by month.
- Event shop plans for an undated event validate with a Monday-first day order.
- The CI calendar checks are unchanged: a current or upcoming merchant without calendar weeks still fails CI until its weeks are authored.

## Capabilities

### Modified Capabilities

- `game-event-shops-dataset`: undated events are served with null `startUtc` and `definitionId`.

## Impact

- `src/TacticusPlanner.GameCatalog`: event shop models, denormalizer and validation.
- `src/TacticusPlanner.Api/Features/EventShopPlans/EventShopPlanCatalogValidator.cs`: day order for an undated event.
- `artifacts/openapi`: `startUtc` and `definitionId` become nullable (catalog schema hash changes).
- No database, migration, configuration or infra changes.
