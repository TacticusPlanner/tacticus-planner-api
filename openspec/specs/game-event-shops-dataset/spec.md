# game-event-shops-dataset Specification

## Purpose

Serves the seasonal event shops (Armageddon Shop and its successors) as a catalog dataset: each event's start, weeks, per-week shop slots, milestone and progress-chest ladders and mission chains, so the client can plan purchases without hand-kept event files.

## Requirements

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

### Requirement: Per-week sources are derived by naming convention

For event key `<Month><Year>` (lower-cased as `<month>_<year>` for datamine keys) and week `N` of `W` calendar weeks, the build SHALL derive:

- `id`: `<month>-<year>` (e.g. `october-2026`), stable and persisted by plans.
- the week's merchant: `<Month><Year>Week<N>EventShop` when it exists, else `<Month><Year>EventShop` (one merchant shared by every week).
- `currency`: the single cost currency `seasonalEventCurrency<Month><Year>`; every variant of the event's merchants SHALL cost it.
- `milestones`: ladder `se_milestones_<month>_<year>` of `gameconfig/loot/tieredProgressRewards.json`, or empty when absent.
- the week's progress chests: ladder `se_progress_chests_<month>_<year>_week<N>` when it exists, else `se_progress_chests_<month>_<year>`, else empty.
- the week's mission prefix: `<month>_<year>_week<N>` when any seasonal quest uses it, else `<month>_<year>`.
- the week's `estimatedWeeklyIncome`: from that occurrence's optional `incomeFree` and `incomeBonusShipment` parameters (see `game-events-calendar-dataset`).

#### Scenario: Per-week merchants

- **WHEN** event key `June2026` has three calendar weeks and merchants `June2026Week1EventShop` .. `June2026Week3EventShop`
- **THEN** week 2's slots come from `June2026Week2EventShop`, its progress chests from `se_progress_chests_june_2026_week2` and its missions from prefix `june_2026_week2`

#### Scenario: Shared merchant

- **WHEN** event key `October2026` has merchant `October2026EventShop` only
- **THEN** every week's slots come from `October2026EventShop`, progress chests from `se_progress_chests_october_2026` and missions from prefix `october_2026`

### Requirement: Served event shop shape

Every served record SHALL have exactly this shape (ids and structured values only; no display names, localisation keys or icon paths):

- `id`: string — e.g. `october-2026`.
- `eventKey`: string — e.g. `October2026`.
- `definitionId`: string or null — the calendar `ShopEvent` definition of its occurrences (e.g. `armageddon-shop`), which the client names; null for an undated event, which the client names from `eventKey`.
- `currency`: string — e.g. `seasonalEventCurrencyOctober2026`.
- `startUtc`: string or null — ISO-8601 UTC instant of week 1's occurrence start; null for an undated event.
- `dayCount`: integer — the served weeks' total length in whole days (`7 × weeks.length` unless the last week is shorter); day `n` (0-based) starts at `startUtc + n days` (for an undated event, an estimate of 7 per known week).
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

#### Scenario: Day count follows a shorter last week

- **WHEN** event key `November2026` has one calendar week from 2026-11-23 00:00 UTC to 2026-11-29 00:00 UTC
- **THEN** its served `dayCount` is 6

#### Scenario: Variants match the daily shop projection

- **WHEN** an event merchant variant has reward `"shards_astraYarrick:25"`, cost `{ type: "seasonalEventCurrencyOctober2026", amount: 45 }`, `maxPurchases` `"5"` and cron `0 0 0 ? * MON,THU,SUN *`
- **THEN** the served variant is `{ reward: { type: "shards_astraYarrick", qty: 25 }, unitId: "astraYarrick", cost: { currency: "seasonalEventCurrencyOctober2026", amount: 45 }, maxPurchasesPerDay: 5, days: ["MON", "THU", "SUN"], ... }`, as the `shops` dataset would serve it

#### Scenario: Income per week

- **WHEN** the week-1 occurrence of `August2026` carries `incomeFree` 750 and `incomeBonusShipment` 1000 and week 2 carries neither
- **THEN** week 1 serves `estimatedWeeklyIncome` `{ free: 750, bonusShipment: 1000 }` and week 2 omits it

#### Scenario: No display strings are served

- **WHEN** the `event-shops` dataset is served
- **THEN** no record, tier, mission or task carries a display name, `locaKey`, `bannerTextKey` or icon field

### Requirement: Reward tiers are derived from ladder entries and chests

For each entry of a `tieredProgressRewards.json` ladder, the served tier's `requiredProgress` SHALL be the entry's `requiredProgress`. When the entry's `chestRewardId` names a key of `gameconfig/loot/chests.json`, the tier's `rewards` SHALL be the rewards of every chest under that key, in source order; otherwise the `chestRewardId` SHALL be parsed as a reward string. A reward string SHALL parse as `type` (min = max = 1), `type:qty` (min = max = qty) or `type:min-max`. The build SHALL fail, naming the event and ladder key, when a reward string cannot be parsed.

#### Scenario: Chest-backed tier

- **WHEN** ladder entry `{ requiredProgress: 30, chestRewardId: "se_progress_chest_october_2026_01" }` names a chest with rewards `["gold:600-660", "shards_astraYarrick:5"]`
- **THEN** the served tier is `{ requiredProgress: 30, rewards: [{ type: "gold", min: 600, max: 660 }, { type: "shards_astraYarrick", min: 5, max: 5 }] }`

#### Scenario: Direct reward tier

- **WHEN** ladder entry `{ requiredProgress: 100, chestRewardId: "seasonalEventCurrencyOctober2026:15" }` names no chest
- **THEN** the served tier is `{ requiredProgress: 100, rewards: [{ type: "seasonalEventCurrencyOctober2026", min: 15, max: 15 }] }`

### Requirement: Missions are selected by name prefix

For a week with mission prefix `P`, `daily` SHALL be the quests of `gameconfig/quests/groups.json` group `seasonal_daily` whose `name` starts with `P_daily_`, ordered by name; `free` SHALL group the quests of `seasonal_seasonal` named `P_free_<n>_chain_<k>` into one chain per `<n>` (chain `id` `P_free_<n>`, chains ordered by `<n>`, missions ordered by following `successorQuests` from the quest that is no other quest's successor); `premium` SHALL be the single chain `P_premium` built the same way from `seasonal_seasonal_premium` quests named `P_premium_chain_<k>`. A mission's `id` SHALL be the quest `name`, its `rewards` its parsed `rewards` strings, and each task's `taskId`, `target` and `parameters` its `name`, `target` and `taskParameters`. A prefix that matches no quest in any of the three groups SHALL serve empty `daily`, `free` and `premium` and SHALL NOT fail the build, so a datamine that prunes an old event's quests keeps the catalog loading.

#### Scenario: Free chain is ordered by successor

- **WHEN** quests `october_2026_free_1_chain_01` (successor `..._02`) through `..._20` exist
- **THEN** week 1 `free` has one chain `october_2026_free_1` with 20 missions in successor order

#### Scenario: Task parameters pass through

- **WHEN** a mission task has `taskParameters` `{ "gameMode": "Survival" }`
- **THEN** the served task carries `parameters` `{ "gameMode": "Survival" }`

#### Scenario: Unmatched prefix serves no missions

- **WHEN** a dated week's mission prefix matches no seasonal quest
- **THEN** the catalog loads and that week's `missions` has empty `daily`, `free` and `premium`

### Requirement: Calendar weeks without an event shop are skipped, and flagged in CI while current

A `ShopEvent` occurrence whose `eventKey` matches no merchant in the pinned datamine SHALL NOT be served as an event shop and SHALL NOT fail startup, so a datamine that drops an old event's merchants keeps the catalog loading and the calendar entry stays. When an event has per-week merchants, it SHALL be served up to (not including) its first calendar week with no `Week<N>` merchant, and not at all when week 1 has none; this SHALL NOT fail startup either. A CI test SHALL fail, naming the event key and week, when an unserved occurrence (no merchant, or cut off by a missing per-week merchant) has not ended at the time the test runs, so a current or upcoming week with no shop (a typo in `eventKey`, or a datamine not yet bumped) is caught before release.

CI SHALL also fail, naming the merchant, when a calendared event has a `Week<N>` merchant with no calendar week `N` (the extra week would not be served and its plans would be rejected), and, naming the event key, when the datamine has a live-event merchant for the current month or a later one whose event key has no calendar weeks at all (the event would be missing while it runs). Event keys listed as known gaps in that test are exempt from the last check until their weeks are authored.

#### Scenario: Dropped past event keeps the catalog loading

- **WHEN** a later datamine no longer contains `June2026Week1..3EventShop` and the calendar still has the `June2026` weeks, which ended in July 2026
- **THEN** the catalog loads, `event-shops` has no `june-2026` record, and the CI test passes

#### Scenario: Missing per-week merchant cuts the event short

- **WHEN** `June2026` has four calendar weeks but only `June2026Week1..3EventShop`
- **THEN** the catalog loads, `june-2026` is served with weeks 1-3 and `dayCount` 21, and the CI test fails naming `June2026` week 4 while that week has not ended

#### Scenario: Datamine merchant week beyond the calendar fails CI

- **WHEN** the datamine has `July2026Week1..3EventShop` and the calendar lists only `July2026` weeks 1 and 2
- **THEN** the CI test fails naming `July2026Week3EventShop`

#### Scenario: Current merchant without calendar weeks fails CI

- **WHEN** the CI test runs on 2026-11-14, the pinned datamine has `November2026EventShop`, and the calendar has no `November2026` week
- **THEN** the test fails naming `November2026`

#### Scenario: Current week without a shop fails CI

- **WHEN** the CI test runs on 2026-11-20 and an occurrence with `eventKey` `November2026` running 2026-11-16 to 2026-11-23 has no matching merchant
- **THEN** the test fails naming `November2026`

### Requirement: Dated events are validated

For every event that has calendar weeks and at least one matching merchant, the build SHALL fail, naming the event key, when: the occurrences' `weekNumber`s are not exactly 1..W; an occurrence does not start at 00:00:00 UTC, or lasts other than exactly 7 days (other than the last week) or other than 1 to 7 whole days (the last week); week `N + 1` does not start exactly when week `N` ends; two occurrences share a `weekNumber`; a variant fails any `shops` variant rule (unparseable reward, cost or cron, empty day list, unresolvable shard unit); or a variant costs a currency other than `seasonalEventCurrency<Month><Year>`.

#### Scenario: Gap between weeks fails the build

- **WHEN** `June2026` week 1 ends 2026-06-29 and week 2 starts 2026-06-30
- **THEN** the catalog build fails naming `June2026`

#### Scenario: Currency mismatch fails the build

- **WHEN** a variant of `October2026EventShop` costs `gems`
- **THEN** the catalog build fails naming `October2026`

### Requirement: The dataset is listed in the public manifest

`event-shops` SHALL be listed in the public catalog manifest with its content hash like every other served dataset, and SHALL be served at `GET /api/v1/game-catalog/event-shops`.

#### Scenario: Manifest lists event-shops

- **WHEN** the manifest is requested
- **THEN** it contains an entry for `event-shops` with a content hash that changes whenever the served array changes
