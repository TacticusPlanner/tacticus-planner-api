## MODIFIED Requirements

### Requirement: Events are discovered from the datamine and dated by the events calendar

The game catalog SHALL serve a dataset keyed `event-shops` as a plain array with one record per seasonal event: the dated events ordered by `startUtc` ascending, then the undated ones ordered by event month. An event SHALL be discovered from the pinned datamine `gameconfig/shop/merchants.json`: every merchant with `liveEventRequired: true` whose key matches `<Month><Year>EventShop` or `<Month><Year>Week<N>EventShop` belongs to event key `<Month><Year>` (e.g. `October2026`). An event SHALL be dated only by the curated events calendar: its weeks are the `ShopEvent` occurrences whose `eventKey` parameter equals the event key, ordered by `weekNumber`. A discovered event with no such occurrence SHALL be served undated (start date to be announced) when its event month is not before the month in which the latest dated event starts (any month when no event is dated); an older one SHALL NOT be served. Neither SHALL fail the build. An undated event's weeks SHALL come from its merchants: week 1 for a shared `<Month><Year>EventShop` merchant, else weeks 1 up to (not including) the first missing `Week<N>` merchant. No other hand-kept event-shop file SHALL exist. Nothing in this dataset recurs or is projected: each dated event is exactly the authored calendar weeks, and it is dated in a catalog version when both that version's datamine has its merchants and its calendar has its weeks.

#### Scenario: A dated event is served

- **WHEN** the datamine has `October2026EventShop` and the calendar has one `ShopEvent` occurrence with `eventKey` `October2026`, `weekNumber` 1, starting 2026-10-26T00:00:00Z
- **THEN** `event-shops` contains a record `october-2026` with `startUtc` 2026-10-26T00:00:00Z and one week

#### Scenario: Undated event is served without a date

- **WHEN** the datamine has `December2026EventShop`, no calendar occurrence has `eventKey` `December2026`, and the latest dated event starts in November 2026
- **THEN** `event-shops` ends with a record `december-2026` with `startUtc` null, `definitionId` null, one week and `dayCount` 7, and the build does not fail

#### Scenario: Undated event is skipped

- **WHEN** the datamine has `May2026EventShop`, no calendar occurrence has `eventKey` `May2026`, and the latest dated event starts in November 2026
- **THEN** no `may-2026` record is served and the build does not fail

#### Scenario: No dated events

- **WHEN** the datamine has no event-shop merchant, so no event is dated or undated
- **THEN** the `event-shops` dataset is served as an empty array

### Requirement: Served event shop shape

Every served record SHALL have exactly this shape (ids and structured values only; no display names, localisation keys or icon paths):

- `id`: string — e.g. `october-2026`.
- `eventKey`: string — e.g. `October2026`.
- `definitionId`: string or null — the calendar `ShopEvent` definition of its occurrences (e.g. `armageddon-shop`), which the client names; null for an undated event, which the client names from `eventKey`.
- `currency`: string — e.g. `seasonalEventCurrencyOctober2026`.
- `startUtc`: string or null — ISO-8601 UTC instant of week 1's occurrence start; null for an undated event.
- `dayCount`: integer — `7 × weeks.length`; day `n` (0-based) starts at `startUtc + n days` (for an undated event, an estimate from its known weeks).
- `milestones`: array of reward tiers (shape below), ordered by `requiredProgress` ascending.
- `weeks`: array ordered by `week` ascending, each:
  - `week`: integer, 1-based, contiguous.
  - `estimatedWeeklyIncome`: optional `{ free: integer, bonusShipment: integer }`; omitted when neither parameter is set (a missing one of the two serves as 0) and for an undated event.
  - `slots`: array of shop slots in source order, each `{ variants: ShopVariant[] }`, where `ShopVariant` is the exact variant projection of the `shops` dataset (`reward {type, qty}`, `unitId?`, `freeOffer? {type, qty}`, `cost {currency, amount}`, `maxPurchasesPerDay`, `weight?`, `days`, `minPowerLevel?`, `maxPowerLevel?`, `lockId?`), derived by the same rules.
  - `progressChests`: array of reward tiers ordered by `requiredProgress` ascending.
  - `missions`: `{ daily: Mission[], free: MissionChain[], premium: MissionChain[] }`.

A reward tier SHALL be `{ requiredProgress: integer, rewards: RewardRange[] }`. A `RewardRange` SHALL be `{ type: string, min: integer, max: integer }`. A `Mission` SHALL be `{ id: string, rewards: RewardRange[], tasks: { taskId: string, target: integer, parameters?: Record<string, string | string[]> }[] }`. A `MissionChain` SHALL be `{ id: string, missions: Mission[] }`.

The client SHALL derive calendar dates, the current event day, past/upcoming status and every display string; the server SHALL send none of them.

#### Scenario: Day count derives from calendar weeks

- **WHEN** event key `June2026` has three calendar weeks
- **THEN** its served `dayCount` is 21

#### Scenario: Variants match the daily shop projection

- **WHEN** an event merchant variant has reward `"shards_astraYarrick:25"`, cost `{ type: "seasonalEventCurrencyOctober2026", amount: 45 }`, `maxPurchases` `"5"` and cron `0 0 0 ? * MON,THU,SUN *`
- **THEN** the served variant is `{ reward: { type: "shards_astraYarrick", qty: 25 }, unitId: "astraYarrick", cost: { currency: "seasonalEventCurrencyOctober2026", amount: 45 }, maxPurchasesPerDay: 5, days: ["MON", "THU", "SUN"], ... }`, as the `shops` dataset would serve it

#### Scenario: Income per week

- **WHEN** the week-1 occurrence of `August2026` carries `incomeFree` 750 and `incomeBonusShipment` 1000 and week 2 carries neither
- **THEN** week 1 serves `estimatedWeeklyIncome` `{ free: 750, bonusShipment: 1000 }` and week 2 omits it

#### Scenario: No display strings are served

- **WHEN** the `event-shops` dataset is served
- **THEN** no record, tier, mission or task carries a display name, `locaKey`, `bannerTextKey` or icon field
