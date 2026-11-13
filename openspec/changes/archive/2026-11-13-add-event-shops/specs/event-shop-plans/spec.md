## Purpose

Persists a player's plan for one seasonal event shop — which offers they intend to buy on which event day, how many, and how many they have already bought — so the planner, shopping list and coverage survive across devices.

## ADDED Requirements

### Requirement: One plan per profile and event shop

The system SHALL persist at most one plan per profile and catalog `event-shops` id. A plan SHALL carry a monotonic `revision`, the catalog version it was last written under (`catalogVersion`) and its cart `entries`. Plans SHALL be scoped to the caller's profile: a plan is never readable or writable by another profile, and deleting the account removes its plans. A stored plan SHALL stay readable after its event leaves the current catalog; only writes require a current catalog event. All endpoints SHALL require an authenticated caller.

#### Scenario: Plan is private to its profile

- **WHEN** profile A has a plan for `october-2026` and profile B requests `GET /event-shop-plans/october-2026`
- **THEN** B receives its own (empty) plan, never A's

#### Scenario: Plan outlives its catalog event

- **WHEN** a plan exists for an event no longer in the current catalog
- **THEN** `GET` returns it with its stored `catalogVersion`, and `PUT` is rejected with 404 `issueCode` `notFound`

### Requirement: A cart entry identifies one offer on one event day

Each entry SHALL carry `week` (1-based), `day` (`MON`..`SUN`), `slotIndex` (0-based within that week's `slots`), `rewardType` (the reward type of the chosen variant), `quantity`, `purchasedQuantity` and an optional `draftAlliance` (`imperial`, `xenos` or `chaos`). At most one entry SHALL exist per (`week`, `day`, `slotIndex`). Cost, pack size, labels and caps SHALL NOT be stored; the client derives them from the catalog.

#### Scenario: Entry shape round-trips

- **WHEN** a plan is saved with entry `{ week: 1, day: "THU", slotIndex: 1, rewardType: "shards_astraYarrick", quantity: 2, purchasedQuantity: 1 }`
- **THEN** the served plan contains exactly that entry with no `draftAlliance`

### Requirement: Entries are replaced as a whole set and validated against the catalog

`PUT /event-shop-plans/{eventId}` with `{ expectedRevision, entries }` SHALL replace the plan's entries with `entries`, creating the plan when none exists. The system SHALL reject with 400 `issueCode` `validation`, naming the offending field, and change nothing, when: the event is not in the current catalog (404 `notFound` instead); `entries` has more than 1000 items; an entry's `week` is outside 1..(event week count); `day` is not `MON`..`SUN`; `slotIndex` is outside that week's slots; no variant of that slot is available on `day` with reward type `rewardType`; `quantity` is outside 1..(that variant's `maxPurchasesPerDay`); `purchasedQuantity` is outside 0..`quantity`; `draftAlliance` is set for a `rewardType` not starting with `draft_`, or is not one of the three alliances; or two entries share (`week`, `day`, `slotIndex`). Variant matching SHALL ignore lock ids and power-level bounds (they are client-resolved). `catalogVersion` SHALL be set to the current catalog version on every successful write.

#### Scenario: Quantity above the cap is rejected

- **WHEN** an entry for a variant with `maxPurchasesPerDay` 5 has `quantity` 6
- **THEN** the response is 400 naming `entries[i].quantity` and the plan is unchanged

#### Scenario: Reward not offered that day is rejected

- **WHEN** an entry names `shards_astraYarrick` on `TUE` in a slot that offers it only on `MON`, `THU` and `SUN`
- **THEN** the response is 400 naming `entries[i].rewardType`

#### Scenario: Purchased above quantity is rejected

- **WHEN** an entry has `quantity` 2 and `purchasedQuantity` 3
- **THEN** the response is 400 naming `entries[i].purchasedQuantity`

#### Scenario: Empty entries clear the cart

- **WHEN** `PUT` sends `entries: []` with the current revision
- **THEN** the plan is kept with no entries and its revision is bumped

### Requirement: One revision guards every mutation

Every `PUT` and `DELETE` SHALL carry `expectedRevision` (0 for a plan that does not exist yet). When it differs from the current revision, the system SHALL make no change and respond 409 in the unified error shape with `issueCode` `eventShopPlanStale` and the current plan under `current`. A concurrent create detected at save time SHALL produce the same response after re-reading. A successful `PUT` SHALL bump the revision by exactly one, except that a `PUT` whose entries equal the stored entries (as a set) SHALL succeed without bumping it.

#### Scenario: Stale write is rejected

- **WHEN** the stored revision is 4 and `PUT` sends `expectedRevision` 3
- **THEN** the response is 409 `eventShopPlanStale` with the revision-4 plan under `current`

#### Scenario: No-op write keeps the revision

- **WHEN** `PUT` sends the stored entries in a different order with the current revision
- **THEN** the response is 200 and the revision is unchanged

### Requirement: Reads, list and delete

`GET /event-shop-plans/{eventId}` SHALL return the stored plan, or — when none is stored — `{ eventId, revision: 0, catalogVersion: null, entries: [] }` without creating one, whether or not the event is in the catalog. `GET /event-shop-plans` SHALL return `{ plans: EventShopPlanResponse[] }` with every stored plan of the caller, ordered by `eventId` (ordinal); events never written are not listed. `DELETE /event-shop-plans/{eventId}?expectedRevision=n` SHALL remove the plan (204), respond 204 when none exists and `n` is 0, and follow the revision rule otherwise. Every plan response SHALL be `EventShopPlanResponse { eventId, revision, catalogVersion, entries }` with `entries` ordered by `week`, then day position in the event's own day order, then `slotIndex`; for an event no longer in the catalog, `day` order SHALL fall back to `MON`..`SUN`. Reads SHALL create nothing and SHALL NOT change any revision.

#### Scenario: Missing plan reads as empty

- **WHEN** no plan is stored for `october-2026`
- **THEN** `GET /event-shop-plans/october-2026` returns revision 0 and no entries, and a following `GET /event-shop-plans` lists nothing

#### Scenario: Entries follow the event's day order

- **WHEN** `october-2026` starts on a Monday and a plan has week-1 entries on `SUN` and `MON`
- **THEN** the `MON` entry is served before the `SUN` entry

### Requirement: Plans travel with account lifecycle features

Deleting the account SHALL delete its event shop plans. The staging transfer export and import SHALL include event shop plans alongside the other per-profile plans, preserving `eventId`, `catalogVersion` and `entries`.

#### Scenario: Account deletion removes plans

- **WHEN** an account with two event shop plans is deleted
- **THEN** no event shop plan rows remain for its profile
