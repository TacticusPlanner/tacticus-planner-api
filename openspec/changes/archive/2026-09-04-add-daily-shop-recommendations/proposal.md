## Why

V2's game catalog has no shop data, so the client cannot reproduce V1's "what should I buy today" shop
recommendations (currently shown on V1's Daily Raids → Today page for the Guild Shop, Guild War Shop,
Rogue Trader, and Crusade Shop). This is the backend half of
[TacticusPlanner/tacticus-planner-apps#75](https://github.com/TacticusPlanner/tacticus-planner-apps/issues/75);
the client-side consumption (a new `shops` IndexedDB store and a dedicated **Shops** page under Dailies)
is tracked as `add-daily-shop-recommendations` in the `tacticus-planner-apps` repo, which this dataset
unblocks.

## What Changes

- Add a new authored raw dataset family to the game catalog — one file per daily shop under
  `Data/shops/` (`shops-guild`, `shops-war`, `shops-rogue-trader`, `shops-crusade`), ported from V1's
  `tacticusplanner/src/fsd/4-entities/shops/data/new-*.json`.
- Serve a single consolidated `shops` dataset (a plain array, one record per shop, keyed by a stable
  shop `id`), following the existing raw-source-family → one-served-dataset pattern used by
  `characters`/`mows`.
- Optimize the V2 shape rather than copy V1's verbatim: parse each product's `reward`/`freeOffer`
  string (`"type:qty"`) into a structured `{ type, qty }` at build time; for character-shard rewards
  (`shards_*` / `mythicShards_*`) additionally resolve the target unit id onto the record; and replace
  each product's Quartz `cronSchedule` string with an explicit day-of-week list (`days`) computed at
  build time — every current cron is a pure day-of-week gate (`0 0 0 ? * <DOW> *`), so this is lossless
  for recommendation purposes and removes cron parsing from every client.
- Preserve the V1 slot/variant structure (`slots[].variants[]`) — it is what makes a slot's outcome
  "guaranteed today" vs. "random today" resolvable — plus each variant's `weight`, `cost`
  (`{ currency, amount }`), `maxPurchasesPerDay`, and `conditions` (`minPowerLevel` / `maxPowerLevel` /
  `lockId`).
- Carry each variant's `lockId` verbatim as an opaque string. Lock semantics (battle-pass-season
  windows, roster/power-level tier gating, per-unit "max legendary" thresholds, "owns any blue-star
  unit") are roster- and time-dependent and stay a client concern, exactly as in V1's
  `shop-resolve.ts`; the catalog does not resolve them.
- Structure character-shard offers so a future feature can treat them as acquisition sources when
  configuring Unlock or Ascend goals (each shard variant exposes `unitId`, `qty`, `currency`,
  `amount`, `maxPurchasesPerDay`, and `days` without any string re-parsing). Wiring that into goal
  configuration is out of scope here.
- Register `shops` in the manifest/hashing pipeline and serve it via a new `AllowAnonymous` endpoint,
  following the existing pattern for the other 16 datasets.
- Extend catalog validation: every product `reward`/`freeOffer`/`cost` parses; every shard variant's
  resolved `unitId` cross-references a served character or MoW; every product resolves to a non-empty
  `days` list; `shops` is required non-empty in the manifest check.
- Shop records carry no display text or icon — the client resolves shop names, currency labels, and
  reward icons from ids, consistent with the served catalog's existing "structural/identity only"
  convention.
- Shop **events** (Armageddon shop, limited-time event shops — a separate V1 page) are **not** in
  scope; only the four always-on daily shops.

## Capabilities

### New Capabilities

- `game-shops-dataset`: the backend daily-shop dataset — authoring shape, the build-time
  reward/cron/unit-id normalization, the opaque-lock convention, and validation.

### Modified Capabilities

- none — this adds a new dataset to the existing game-catalog pipeline; no existing served dataset's
  requirements change and `SchemaVersion` is not bumped (purely additive, same as
  `add-game-events-calendar-dataset`).

## Impact

- **Data**: new `Data/shops/shops-guild.json`, `shops-war.json`, `shops-rogue-trader.json`,
  `shops-crusade.json` (embedded by the existing `Data/**/*.json` glob).
- **Models**: new raw source keys (`shops-guild`, `shops-war`, `shops-rogue-trader`,
  `shops-crusade`) and a new served key (`shops`) in `Models/GameCatalogDatasets.cs`; new served-view
  record(s) for a shop and its slots/variants; the raw model for the V1-shaped source files (internal
  to denormalization).
- **Denormalization**: new `Denormalization/ShopsDenormalizer.cs` (`partial class
  GameCatalogDenormalizer`) implementing reward/free-offer parsing, unit-id resolution, and the
  cron → `days` reduction.
- **Validation**: `Validation/*.cs` extended for reward/cost parse checks, shard `unitId`
  cross-references, and non-empty `days`; `ManifestValidation` extended to require `shops` non-empty.
- **Hashing/manifest**: `Utils/GameCatalogHashing.cs` registers `shops`; the Verify-guarded manifest
  snapshot test updates accordingly.
- **Endpoints**: `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs` gains a
  `GET /api/v1/game-catalog/shops` endpoint via the existing `ServedDatasetEndpoint<T>` pattern.
- No breaking change to any of the 16 existing served datasets or their schema version. Existing
  clients unaware of `shops` are unaffected; new clients pick it up through the existing manifest-diff
  sync.
