## Context

See `proposal.md` for motivation. Relevant current state (see this repo's `game-catalog-data` skill for
the full pipeline):

- Raw sources live under `Data/**`, discovered by convention (dataset key `foo-bar` → embedded
  `foo-bar.json`, matched by leaf filename; subfolders are irrelevant). `Models/GameCatalogDatasets.cs`
  is the registry of raw source keys and the `Served` list. `csproj` already globs `Data/**/*.json`.
- `Denormalization/*.cs` is a `partial class GameCatalogDenormalizer`, one file per entity, building
  served views from raw collections. `Validation/*.cs` is a `partial class GameCatalogValidator`
  running over the **raw** snapshot at load, throwing on any error (fail-fast, no partial catalog).
- Every served dataset today carries only structural/identity fields — no `icon`/`iconId`/`wikiLink`
  anywhere. Shop records follow the same rule.
- `GameCatalogRelease.SchemaVersion` is bumped only on a breaking shape change to an existing served
  dataset; adding a new dataset key is not itself a breaking change (precedent:
  `add-game-events-calendar-dataset`).
- V1's shop model (`tacticusplanner/src/fsd/4-entities/shops/`) is: a `ShopData` per shop with
  `products: ShopProduct[][]` (slots × variants); each `ShopProduct` has `weight?`, `conditions
  {minPowerLevel?, maxPowerLevel?, lockId?}`, `cronSchedule` (Quartz), `reward` (`"type:qty"`),
  `freeOffer?` (`"type:qty"`), `maxPurchases?` (string), `cost {type, amount}`. Resolution
  (`shop-resolve.ts`): a slot's variants are filtered by day-of-week (cron field index 5) and by PL /
  `lockId`, then grouped by reward type — one resulting type ⇒ "guaranteed", several ⇒ "random".
- Inspection of all four current source files: every `cronSchedule` is `0 0 0 ? * <DOW> *` — a pure
  day-of-week gate, no time-of-day variation. Cost `type` values are single per shop (`guildCredits`,
  `guildWarCurrency`, `elderShopCurrency`, `crusadeCurrency`). `lockId` vocabulary is large
  (per-hero `lock_hero_not_maxed_out_*` / `lock_max_legendary_*`, Rogue Trader relic slots,
  bp-season windows, crusade "owns unit at mythic") and V1 resolves only a subset client-side.

## Goals / Non-Goals

**Goals:**

- Land one new served dataset key (`shops`) inside the existing pipeline shape with no new
  architectural pattern — raw family → denormalizer → validation → hashing → `ServedDatasetEndpoint<T>`.
- Move the two build-time-stable transforms off every client: `"type:qty"` parsing and
  cron → day-of-week reduction. Do them once, here.
- Make the served shape sufficient for two consumers: the near-term "buy today" recommendations
  (day + PL + lock filtering, guaranteed-vs-random grouping) and the later "shard offer as goal
  acquisition source" use case (structured `unitId` + cost + cadence + cap).

**Non-Goals:**

- Resolving lock semantics server-side (bp-season math, roster tiers, per-unit thresholds). These are
  roster/time-dependent; `lockId` passes through opaque and stays a client concern.
- Any client-side work (IndexedDB store, selectors, Shops page) — tracked in
  `tacticus-planner-apps`'s `add-daily-shop-recommendations`.
- Shop **events** (Armageddon / seasonal event shops) — a separate V1 page, separate future change.
- An authoring/refresh pipeline. `Data/shops/*.json` is hand-maintained against the V1 datamine like
  every other catalog source.

## Decisions

**1. Four raw source files, one per shop, consolidated into one served `shops` array.**
Mirrors the established `units-{faction}.json` → `characters` pattern: raw keys `shops-guild`,
`shops-war`, `shops-rogue-trader`, `shops-crusade` under `Data/shops/`, each a near-verbatim port of
the corresponding V1 `new-*.json`; the denormalizer emits one served record per shop into the `shops`
array, keyed by a stable `id` (`guild`/`war`/`rogue-trader`/`crusade`).
- _Alternative considered_: a single `shops.json`. Rejected — one file per shop keeps each port
  diffable against its distinct V1 source file and matches the split-family convention already used
  for units/npcs/equipment/campaign-battles.
- _Alternative considered_: serve each shop as its own dataset key. Rejected — four tiny datasets add
  four manifest/hash/endpoint entries for data that is always consumed together.

**2. Preserve the slot/variant tree; do not pre-compute a `guaranteed` flag.**
"Guaranteed today" depends on the *day* and on *lock resolution*, and lock resolution is
roster/time-dependent (Decision 4). Baking a flag at build time would either be wrong (locks ignored)
or impossible (roster unknown). The served variant carries `days`, `weight`, `conditions`, and the
parsed `reward` — everything the client's existing `groupSlotsByRewardType` logic needs to decide
guaranteed-vs-random for a given day and roster.

**3. Normalize `reward`/`freeOffer`/`cost` and the cron at build time.**
`reward`/`freeOffer` → `{ type, qty }` (absent qty ⇒ 1); `cost` → `{ currency, amount }`;
`maxPurchases` string → `maxPurchasesPerDay` number (absent ⇒ 1); `cronSchedule` → `days: DayOfWeek[]`
by taking cron field index 5, splitting on `,`, and expanding `*`/`?` to all seven days. All are pure
functions of the source with no runtime input, so doing them here removes string/cron parsing from
every client and makes them validation targets (a malformed reward/cost/cron fails the build).
- _Alternative considered_: keep the raw strings and let the client parse. Rejected — this is exactly
  the kind of representation churn the served layer exists to absorb, and it would fork the parsing
  logic across every client language.

**4. `lockId` is carried verbatim; the catalog never interprets or filters on it.**
The lock vocabulary is large, partly per-hero, partly time-windowed (bp-season), and V1's own
resolver (`lockIsActive` / `resolveEventLockId`) only handles a subset and needs the live roster
(stars per unit, "owns any blue-star unit", PL tier). Resolving it server-side would require shipping
roster state into catalog denormalization, which the catalog has no access to and should not. The
client keeps its resolver; the catalog's job is faithful pass-through. Unknown lock ids are not an
error — a new datamine can introduce one and the build still succeeds.

**5. Character-shard offers get a resolved `unitId` field; that is the only cross-reference.**
`shards_<unitId>` / `mythicShards_<unitId>` → also emit `unitId`, validated against the served
character + MoW id sets (same failure mode as every other cross-reference check). This is what makes
the "shard offer as acquisition source for an Unlock/Ascend goal" use case land later without
re-parsing reward types. Non-shard reward types (`upg*`, `itemAscensionResource_*`, `gold`, `xp*`,
`draft_*`, `dust`, relics `R…`/`I…`) are left as opaque type strings — the client already maps those
to icons/labels by id, and none of them need a catalog-side reference check.

**6. No `SchemaVersion` bump.**
Purely additive new key. The per-dataset manifest hashing means existing clients ignore `shops`
until their client-side change ships; `shops`'s content is fully deterministic from its source files
(no load-time "now" dependency, unlike `events-calendar`), so its dataset hash is stable and the
manifest snapshot test gets one new, deterministic entry.

**7. Rogue Trader's "penultimate slot" quirk stays client-side.**
V1's `RogueTraderService.resolvePenultimateForDay` picks one specific RT slot (`products.at(-2)`) for
the Today recommendations and strips its conditions. That is a V1 presentation choice, not a data
fact. The catalog serves all RT slots faithfully in source order; which slot(s) the Shops page
surfaces is decided in the client change.

## Risks / Trade-offs

- [Risk] The cron → `days` reduction assumes every `cronSchedule` is a pure day-of-week gate. A
  future datamine could introduce a time-of-day or day-of-month restriction. → Mitigation: the
  reducer only reads field index 5 and the validator fails the build on an empty `days` result; if a
  non-DOW cron ever appears, add explicit handling then rather than silently mis-reducing. Documented
  in `ShopsDenormalizer`.
- [Risk] `lockId` pass-through means the served data alone cannot tell a naive consumer which
  variants are actually available for a given roster — it must run lock resolution. → Mitigation: this
  is inherent to the domain and already true in V1; the client change owns porting `shop-resolve.ts`.
- [Trade-off] Not pre-computing `guaranteed` means every client re-derives it. Accepted — the grouping
  logic is small and roster-dependent, so a single canonical server answer isn't possible anyway.
- [Risk] The four V1 source files carry reward types the V2 catalog has no other dataset for (relics
  `R…`/`I…`, `dust`, `mythicDust`, `expeditionSpeedUp`, `itemsCommon…itemsMythic`). → Mitigation:
  only `shards_*`/`mythicShards_*` are cross-referenced; all other types are opaque strings, so an
  unmapped type is a client display concern, not a build failure.
- [Risk] Hand-porting four files risks transcription drift from V1. → Mitigation: the ports are
  near-verbatim (structure preserved), and validation (reward/cost parse, shard unit-id resolution,
  non-empty days, non-empty dataset) catches the likely mistakes at load.

## Migration Plan

Purely additive: new raw files, one new served key, no change to any existing dataset's shape or
`SchemaVersion`. Ships as a normal catalog content release. Existing clients unaware of `shops` are
unaffected; the `tacticus-planner-apps` `add-daily-shop-recommendations` change picks it up through
the existing manifest-diff sync once it lands. Rollback is a plain revert; no data backfill either
direction.
