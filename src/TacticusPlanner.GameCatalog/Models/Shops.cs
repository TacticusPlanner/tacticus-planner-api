using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

// ---- raw authored shape (internal to denormalization; bound from Data/shops/shops-*.json) ---------

/// <summary>
/// One authored daily-shop source file (V1 <c>ShopData</c> shape). Consolidated into the served
/// <see cref="GameCatalogShopView"/> by <c>Denormalization/ShopsDenormalizer.cs</c>. Authored fields the
/// served view has no use for (per-variant <c>bannerTextKey</c> display strings) are intentionally left
/// unbound and dropped on deserialize.
/// </summary>
public sealed record GameCatalogRawShop(
    string DisplayLocation,
    GameCatalogRawShopRefreshCost? RefreshCost,
    bool RefreshWithAdWatch,
    int AllowedRefreshesPerDay,
    IReadOnlyList<IReadOnlyList<GameCatalogRawShopVariant>> Products);

public sealed record GameCatalogRawShopRefreshCost(string ResourceType, double Amount);

/// <summary>
/// One product variant inside a slot. <c>Reward</c> / <c>FreeOffer</c> are raw <c>"type"</c> or
/// <c>"type:qty"</c> strings; <c>CronSchedule</c> is a Quartz expression (every current one is a pure
/// day-of-week gate <c>0 0 0 ? * &lt;DOW&gt; *</c>); <c>MaxPurchases</c> is a stringified integer or absent.
/// All three are normalized at build time — see <c>Utils/ShopNormalization.cs</c>.
/// </summary>
public sealed record GameCatalogRawShopVariant(
    double? Weight,
    GameCatalogRawShopConditions? Conditions,
    string CronSchedule,
    string Reward,
    string? FreeOffer,
    string? MaxPurchases,
    GameCatalogRawShopCost? Cost);

public sealed record GameCatalogRawShopConditions(int? MinPowerLevel, int? MaxPowerLevel, string? LockId);

public sealed record GameCatalogRawShopCost(string Type, double Amount);

// ---- served view (public catalog surface) --------------------------------------------------------

/// <summary>
/// One always-on daily shop (<c>guild</c> / <c>war</c> / <c>rogue-trader</c> / <c>crusade</c>). Structural
/// and identity fields only — no shop name, currency label, reward label, or icon; the client resolves
/// those from <see cref="Id"/> and the per-variant reward / cost currency ids. See specs/game-shops-dataset.
/// </summary>
public sealed record GameCatalogShopView(
    string Id,
    string DisplayLocation,
    bool RefreshWithAdWatch,
    int AllowedRefreshesPerDay,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GameCatalogShopRefreshCostView? RefreshCost,
    IReadOnlyList<GameCatalogShopSlotView> Slots);

public sealed record GameCatalogShopRefreshCostView(string ResourceType, double Amount);

/// <summary>
/// One rotating shop slot, exposing its product variants in source order. When more than one variant is
/// available on the same day and they resolve to different reward types the slot's outcome is randomized;
/// when the day-matching variants all resolve to the same reward type it is guaranteed. The catalog does
/// not pre-compute that flag — it depends on the day and on client-side lock resolution.
/// </summary>
public sealed record GameCatalogShopSlotView(IReadOnlyList<GameCatalogShopVariantView> Variants);

/// <summary>
/// One product variant. <see cref="UnitId"/> is set only for character-/mythic-shard rewards (the id
/// embedded in the reward type, cross-referenced against served characters and MoWs at load).
/// <see cref="Days"/> is the explicit day-of-week list (<c>MON</c>..<c>SUN</c>, weekday order) reduced from
/// the source cron. Optional fields are omitted from the payload when the source omits them.
/// </summary>
public sealed record GameCatalogShopVariantView(
    GameCatalogShopRewardView Reward,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UnitId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GameCatalogShopRewardView? FreeOffer,
    GameCatalogShopCostView Cost,
    int MaxPurchasesPerDay,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Weight,
    IReadOnlyList<string> Days,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MinPowerLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxPowerLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LockId);

/// <summary>A reward or free bundled offer, parsed from the source <c>"type"</c> / <c>"type:qty"</c> string (absent quantity ⇒ 1).</summary>
public sealed record GameCatalogShopRewardView(string Type, int Qty);

/// <summary>A variant's cost: the game's own currency id string and a numeric amount.</summary>
public sealed record GameCatalogShopCostView(string Currency, double Amount);
