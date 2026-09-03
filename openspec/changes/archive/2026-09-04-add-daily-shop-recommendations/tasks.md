## 1. Raw data authoring

- [x] 1.1 Add `Data/shops/shops-guild.json`, `shops-war.json`, `shops-rogue-trader.json`, and `shops-crusade.json`, ported near-verbatim from V1's `tacticusplanner/src/fsd/4-entities/shops/data/new-guild-shop.json`, `new-war-shop.json`, `new-rogue-trader.json`, `new-crusade-shop-data.json` (slot/variant structure preserved; `weight`, `conditions`, `cronSchedule`, `reward`, `freeOffer`, `maxPurchases`, `cost`, and shop-level `displayLocation`/`refreshCost`/`refreshWithAdWatch`/`allowedRefreshesPerDay` kept). Verify each file is valid JSON and embedded (build output contains it via the `Data/**/*.json` glob).
- [x] 1.2 Confirm every `reward`/`freeOffer` string in the four files matches the `type` or `type:qty` grammar and every `cronSchedule` is a day-of-week-only Quartz expression (`0 0 0 ? * <DOW> *`); note any exception in `ShopsDenormalizer` for handling in task 3.

## 2. Registry & models

- [x] 2.1 Add raw source keys `shops-guild`, `shops-war`, `shops-rogue-trader`, `shops-crusade` and served key `shops` (const + `Served` list entry) to `Models/GameCatalogDatasets.cs`. Verify `GameCatalogDatasets.Served` enumerates `shops`.
- [x] 2.2 Add the raw model bound from the V1-shaped source files (shop → `products: RawSlot[][]`, each variant with `weight?`, `conditions`, `cronSchedule`, `reward`, `freeOffer?`, `maxPurchases?`, `cost`) — internal to denormalization, not a served view.
- [x] 2.3 Add the served-view records: `GameCatalogShopView` (`id`, `displayLocation`, `refreshWithAdWatch`, `allowedRefreshesPerDay`, `refreshCost?` `{resourceType, amount}`, `slots: ShopSlotView[]`), `ShopSlotView` (`variants: ShopVariantView[]`), `ShopVariantView` (`reward` `{type, qty}`, `unitId?`, `freeOffer?` `{type, qty}`, `cost` `{currency, amount}`, `maxPurchasesPerDay`, `weight?`, `days: string[]`, `minPowerLevel?`, `maxPowerLevel?`, `lockId?`). No display-text/icon fields. Verify against the "Served shop payload shape" and "Shop slots preserve the game's slot-and-variant structure" scenarios in the spec.

## 3. Denormalization

- [x] 3.1 Add `Denormalization/ShopsDenormalizer.cs` (`partial class GameCatalogDenormalizer`) producing the `shops` array, one record per source file, `id` = `guild` / `war` / `rogue-trader` / `crusade`. Verify one record per shop with the expected ids ("All four daily shops are present" scenario).
- [x] 3.2 Implement reward/free-offer parsing to `{ type, qty }` (missing `:qty` ⇒ 1) and cost parsing to `{ currency, amount }` (`cost.type` → `currency`); `maxPurchases` string → `maxPurchasesPerDay` number (absent ⇒ 1). Verify with a denormalizer unit test covering explicit-qty, implied-qty, free-offer, and default-cap cases.
- [x] 3.3 Implement the cron → `days` reduction: read Quartz field index 5, split on `,`, expand `*`/`?` to all seven `MON..SUN`. Verify with a unit test covering a `MON,THU` restriction and an unrestricted (`*`) variant.
- [x] 3.4 For `shards_<id>` / `mythicShards_<id>` reward types, resolve and emit `unitId`; leave it unset for every other reward type. Verify with a unit test asserting a shard variant carries `unitId` and a non-shard variant does not.
- [x] 3.5 Pass `weight`, `minPowerLevel`, `maxPowerLevel`, and `lockId` through unchanged (omit when the source omits them). Verify a variant with `lock_crusade_shop_owns_unit_at_mythic` is served with that exact string and not dropped.

## 4. Validation

- [x] 4.1 Extend `Validation/*.cs`: fail the build if any variant's `reward`, `freeOffer`, or `cost` cannot be parsed into type/qty resp. currency/amount. Verify with a validator unit test using a deliberately malformed fixture (and the passing case).
- [x] 4.2 Add a cross-reference check: every shard variant's resolved `unitId` must match a served character or MoW id; throw at load otherwise. Verify with a unit test for the unresolvable-id failure and the resolvable-id success.
- [x] 4.3 Fail the build if any variant reduces to an empty `days` list. Verify with a unit test.
- [x] 4.4 Extend `ManifestValidation` to require `shops` non-empty, consistent with the existing non-empty-dataset check for other served datasets. Verify by asserting an empty `shops` snapshot throws.

## 5. Hashing & manifest

- [x] 5.1 Register `shops` in `Utils/GameCatalogHashing.cs` for per-dataset hashing and inclusion in `SourceHash`. Verify the manifest response includes a `shops` hash entry.

## 6. Endpoints

- [x] 6.1 Add a `GET /api/v1/game-catalog/shops` `AllowAnonymous` endpoint via the existing `ServedDatasetEndpoint<T>` pattern in `Features/GameCatalog/GetGameCatalogDatasetEndpoints.cs`. Do not add endpoints for the raw per-shop keys. Verify with an endpoint test asserting a 200 with one record per shop and no auth required.

## 7. Tests & verification

- [x] 7.1 Add `TacticusPlanner.GameCatalog.Tests` coverage for the denormalizer (reward/free-offer/cost parsing, cron→days, shard `unitId` resolution, purchase-cap default) and the validator failures/successes from group 4.
- [x] 7.2 `dotnet build TacticusPlanner.slnx -c Release` — confirm startup load + validation pass with the four seeded source files.
- [x] 7.3 `dotnet test TacticusPlanner.slnx -c Release --no-build`; review and promote the Verify manifest snapshot diff (`GameCatalogSnapshotTests` `.received.txt` → `.verified.txt`) to include the new deterministic `shops` dataset hash. Note any pre-existing unrelated failures explicitly.
- [x] 7.4 `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore` passes.
- [x] 7.5 Confirm `SchemaVersion` is unchanged and no existing served dataset's payload shape changed (diff the rest of the manifest snapshot — only the added `shops` entry and the aggregate `sourceHash` should move).
