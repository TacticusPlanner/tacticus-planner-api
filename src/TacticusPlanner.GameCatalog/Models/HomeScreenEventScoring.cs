using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// Raw <c>home-screen-event-scoring</c> record: the scoring of one Home Screen Event definition, imported
/// from V1's home-screen-event datamine (see the game-catalog-data skill). Never served on its own — the
/// denormalizer merges it into the definition's served <c>config.hse</c> (see
/// <c>Denormalization/EventsDenormalizer.cs</c> and specs/home-screen-event-scoring).
/// </summary>
public sealed record GameCatalogHseScoring(string DefinitionId, IReadOnlyList<GameCatalogHseVariant> Variants);

/// <summary>The served <c>config.hse</c> object of a Home Screen Event definition.</summary>
public sealed record GameCatalogHseConfig(IReadOnlyList<GameCatalogHseVariant> Variants);

/// <summary>
/// One ladder/scoring variant of a definition. An occurrence resolves to the variant keyed by its
/// <c>hseVariant</c> parameter, else by its <c>targetFactionId</c> parameter, else <c>default</c>.
/// <see cref="Scope"/> is <c>Personal</c> or <c>Global</c> (milestones count every player's points).
/// </summary>
public sealed record GameCatalogHseVariant(
    string VariantKey,
    string Scope,
    IReadOnlyList<GameCatalogHseTier> Tiers,
    IReadOnlyList<GameCatalogHseTracker> Trackers,
    IReadOnlyList<GameCatalogHseModifier> Modifiers);

/// <summary>
/// One power tier's reward ladder (<c>High</c>/<c>Mid</c>/<c>Low</c>/<c>Default</c>). Milestone points are
/// cumulative; <see cref="Repeat"/> is the endless step after the last milestone (its points are per repeat).
/// </summary>
public sealed record GameCatalogHseTier(
    string Tier,
    IReadOnlyList<GameCatalogHseMilestone> Milestones,
    GameCatalogHseRepeat? Repeat);

public sealed record GameCatalogHseMilestone(int RequiredPoints, string RewardId, int Quantity);

public sealed record GameCatalogHseRepeat(int Points, string RewardId, int Quantity, int? Cap);

/// <summary>
/// How points are earned: <see cref="Points"/> per unit of <see cref="Kind"/> (a kill, a wave, a battle, a
/// donation, a dispatch) in the allowed game modes (catalog mode names), counting only enemies passing
/// <see cref="Enemy"/> and only the player's units passing <see cref="Player"/>.
/// </summary>
public sealed record GameCatalogHseTracker(
    string Kind,
    int Points,
    IReadOnlyDictionary<string, int>? PointsByRarity,
    GameCatalogHseRestriction? GameModes,
    GameCatalogHseUnitFilter? Enemy,
    GameCatalogHseUnitFilter? Player);

/// <summary>A value passes when it is not in <see cref="Disallowed"/> and, if <see cref="Allowed"/> is set, is in it.</summary>
public sealed record GameCatalogHseRestriction(IReadOnlyList<string>? Allowed, IReadOnlyList<string>? Disallowed);

/// <summary><see cref="Tags"/> is matched against a unit's traits or its alliance.</summary>
public sealed record GameCatalogHseUnitFilter(
    GameCatalogHseRestriction? Tags,
    GameCatalogHseRestriction? FactionIds,
    IReadOnlyList<string>? UnitIds);

/// <summary>
/// An event bonus. <c>UnitXp</c> carries <see cref="Multiplier"/> or <see cref="MultiplierByRarity"/> and
/// <see cref="SameFactionTeam"/>; <c>StatBoost</c> carries the three percentages. Fields of the other kind
/// are omitted from the payload.
/// </summary>
public sealed record GameCatalogHseModifier(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Multiplier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, double>? MultiplierByRarity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? SameFactionTeam,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? HealthPct,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DamagePct,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ArmourPct,
    GameCatalogHseRestriction? GameModes,
    GameCatalogHseUnitFilter? Player);
