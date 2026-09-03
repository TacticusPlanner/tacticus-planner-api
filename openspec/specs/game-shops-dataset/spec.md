# game-shops-dataset Specification

## Purpose
Provides the backend game catalog's daily-shop data — the four always-on shops (Guild Shop, Guild War
Shop, Rogue Trader, Crusade Shop), their per-day rotating slots, and structured reward/cost/condition
data — so downstream clients can reproduce "what is worth buying today" recommendations and, later,
treat character-shard offers as goal acquisition sources, without parsing Quartz cron strings or
`"type:qty"` reward strings themselves.

## Requirements

### Requirement: The catalog serves one consolidated `shops` dataset

The game catalog SHALL serve a dataset keyed `shops` as a plain array with one record per daily shop,
each identified by a stable, human-readable `id` (`guild`, `war`, `rogue-trader`, `crusade`). It SHALL
be built from four authored raw source files (one per shop) and SHALL be the only shop dataset exposed
publicly; the raw per-shop source files SHALL NOT be served directly.

#### Scenario: Shops dataset is served without authentication

- **WHEN** a client requests the `shops` endpoint
- **THEN** an array with one record per daily shop is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: Shops dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `shops`

#### Scenario: Raw per-shop sources have no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving a single raw per-shop source file directly

#### Scenario: All four daily shops are present

- **WHEN** the `shops` dataset is served
- **THEN** it contains exactly the records with ids `guild`, `war`, `rogue-trader`, and `crusade`

### Requirement: Shop records carry no display text or icon

Consistent with every other served catalog dataset, a `shops` record SHALL NOT include a shop display
name, currency label, reward label, icon, icon id, or wiki link. Only structural/identity fields exist
(shop `id`, the game's own `displayLocation` string, refresh metadata, slots, and per-variant reward /
cost / condition data referenced by id).

#### Scenario: Served shop payload shape

- **WHEN** a `shops` record is served
- **THEN** it contains only structural/identity fields and no display-text or icon field

### Requirement: Shop slots preserve the game's slot-and-variant structure

Each shop record SHALL expose its rotating slots as an ordered list, and each slot SHALL expose an
ordered list of variants. A slot with more than one variant that could appear on the same day
represents a randomized outcome; a slot whose day-matching variants all resolve to the same reward
type represents a guaranteed outcome. The dataset SHALL carry enough per-variant data for a client to
make that determination itself (see the reward, day, and condition requirements below) without the
catalog pre-computing a "guaranteed" flag.

#### Scenario: Multi-variant slot is preserved

- **WHEN** a source slot lists several mutually-exclusive product variants for a day
- **THEN** the served slot lists every one of those variants, in source order, each with its own reward, cost, days, weight, and conditions

#### Scenario: Single-variant slot is preserved

- **WHEN** a source slot lists exactly one product for a day
- **THEN** the served slot lists that one variant

### Requirement: Reward and free-offer strings are parsed into structured values

Every variant's reward SHALL be served as a structured value `{ type, qty }` parsed from the source
`"type"` or `"type:qty"` string (an absent quantity meaning `1`). When a variant additionally grants a
free bundled offer, it SHALL be served as a structured `{ type, qty }` value the same way. The
catalog build SHALL fail if a reward or free-offer string cannot be parsed.

#### Scenario: Reward with explicit quantity

- **WHEN** a source variant's reward is `"shards_eldarFarseer:5"`
- **THEN** the served variant's reward is `{ type: "shards_eldarFarseer", qty: 5 }`

#### Scenario: Reward with implied quantity

- **WHEN** a source variant's reward is `"itemAscensionResource_Mythic"`
- **THEN** the served variant's reward is `{ type: "itemAscensionResource_Mythic", qty: 1 }`

#### Scenario: Free bundled offer is parsed

- **WHEN** a source variant carries `"freeOffer": "draft_machinesOfWarTokens:10"`
- **THEN** the served variant exposes a structured free offer `{ type: "draft_machinesOfWarTokens", qty: 10 }`

#### Scenario: Unparseable reward fails the build

- **WHEN** a source variant's reward or free-offer string cannot be parsed into a type and quantity
- **THEN** the catalog build fails validation

### Requirement: Character-shard offers expose a resolved unit id

When a variant's reward type denotes character shards or mythic character shards (`shards_<unitId>` /
`mythicShards_<unitId>`), the served variant SHALL additionally expose the resolved `unitId` as a
first-class field, so a consumer can use the offer as a goal acquisition source (target unit, shard
quantity, currency, amount, purchase cap, and cadence) without re-parsing the reward type. The catalog
build SHALL fail if a shard offer's `unitId` does not resolve to a served character or MoW.

#### Scenario: Shard offer carries its unit id

- **WHEN** a served variant's reward type is `shards_eldarFarseer`
- **THEN** the variant exposes `unitId` = `eldarFarseer`, which resolves to a served character or MoW

#### Scenario: Unresolvable shard unit id fails the build

- **WHEN** a shard offer's embedded unit id does not match any served character or MoW
- **THEN** the catalog build fails validation

#### Scenario: Non-shard rewards have no unit id

- **WHEN** a served variant's reward type is not a character-shard type (e.g. an upgrade material, forge badge, gold, or XP)
- **THEN** the variant exposes no `unitId`

### Requirement: Availability is served as an explicit day-of-week list

Each variant SHALL express the days it is available as an explicit list of day-of-week values
(`MON`..`SUN`) computed at build time from the source Quartz `cronSchedule`. A cron with no
day-of-week restriction SHALL be reduced to the full seven-day list. The served dataset SHALL NOT
contain raw cron strings. The catalog build SHALL fail if a variant reduces to an empty day list.

#### Scenario: Day-restricted variant

- **WHEN** a source variant's cron restricts it to `MON,THU`
- **THEN** the served variant's `days` list is exactly `["MON", "THU"]`

#### Scenario: Unrestricted variant

- **WHEN** a source variant's cron places no day-of-week restriction on it
- **THEN** the served variant's `days` list contains all seven days

#### Scenario: Empty day list fails the build

- **WHEN** a source variant's cron reduces to no available day
- **THEN** the catalog build fails validation

### Requirement: Cost, purchase caps, weight, and power-level conditions are preserved

Each variant SHALL carry its cost as `{ currency, amount }` (currency being the game's own currency
id string), its per-day purchase cap as a number (defaulting to `1` when the source omits it), its
random-draw `weight` when the source provides one, and its power-level bounds (`minPowerLevel` /
`maxPowerLevel`) when the source constrains them. The catalog build SHALL fail if a cost cannot be
parsed into a currency and a numeric amount.

#### Scenario: Cost and cap are preserved

- **WHEN** a source variant costs `525` guild credits with `"maxPurchases": "2"`
- **THEN** the served variant's cost is `{ currency: "guildCredits", amount: 525 }` and its per-day cap is `2`

#### Scenario: Default purchase cap

- **WHEN** a source variant omits a purchase cap
- **THEN** the served variant's per-day cap is `1`

#### Scenario: Power-level bounds are preserved

- **WHEN** a source variant restricts itself to `minPowerLevel` 20
- **THEN** the served variant carries `minPowerLevel` 20

### Requirement: Lock ids are carried verbatim as opaque strings

When a source variant carries a `lockId`, the served variant SHALL carry that same string unchanged.
The catalog SHALL NOT interpret, resolve, validate, or drop variants based on lock semantics —
battle-pass-season windows, roster/power-level tiers, per-unit "max legendary" thresholds, and
"owns any blue-star unit" gating are all roster- and time-dependent and remain a client
responsibility.

#### Scenario: Lock id passes through unchanged

- **WHEN** a source variant carries `"lockId": "lock_crusade_shop_owns_unit_at_mythic"`
- **THEN** the served variant carries the identical `lockId` string and is not filtered out by the catalog

#### Scenario: Unrecognized lock id is not an error

- **WHEN** a source variant carries a `lockId` the catalog has never seen before
- **THEN** the catalog build does not fail and the variant is served with that `lockId` intact

### Requirement: Refresh metadata is preserved per shop

Each shop record SHALL carry its refresh metadata: the `displayLocation` id, whether the shop can be
refreshed by watching an ad, the number of free refreshes allowed per day, and the refresh cost as
`{ resourceType, amount }` when the source defines one.

#### Scenario: Refresh metadata round-trips

- **WHEN** a source shop allows one free refresh per day, permits ad-watch refresh, and charges 50 gems per further refresh
- **THEN** the served shop record reflects all three of those values

### Requirement: Shop events are out of scope

The `shops` dataset SHALL cover only the four always-on daily shops. Limited-time event shops
(Armageddon shop, seasonal event shops) SHALL NOT be included in this dataset.

#### Scenario: No event-shop records

- **WHEN** the `shops` dataset is served
- **THEN** it contains no record for a limited-time or seasonal event shop

### Requirement: Adding the shops dataset does not bump the schema version

Introducing `shops` SHALL be purely additive: no existing served dataset's shape changes and the
catalog `SchemaVersion` is not incremented. A client unaware of `shops` SHALL be unaffected.

#### Scenario: Existing datasets and schema version unchanged

- **WHEN** the `shops` dataset is added to the catalog
- **THEN** the catalog `SchemaVersion` is unchanged and every previously served dataset's payload shape is unchanged
