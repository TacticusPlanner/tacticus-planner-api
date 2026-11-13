using System.Text.Json;
using System.Text.RegularExpressions;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The parameters a calendar <c>ShopEvent</c> occurrence (one week of a seasonal event shop) carries, and their
/// readers. Shared by occurrence validation and the <c>event-shops</c> denormalizer so both read them one way.
/// See specs/game-events-calendar-dataset and specs/game-event-shops-dataset.
/// </summary>
public static partial class ShopEventParameters
{
    /// <summary>The event-definition <c>type</c> of a seasonal event shop week.</summary>
    public const string DefinitionType = "ShopEvent";

    public const string WeekNumber = "weekNumber";

    /// <summary>The datamine event key <c>&lt;Month&gt;&lt;Year&gt;</c> (e.g. <c>October2026</c>) the week belongs to.</summary>
    public const string EventKey = "eventKey";

    public const string IncomeFree = "incomeFree";

    public const string IncomeBonusShipment = "incomeBonusShipment";

    public static readonly IReadOnlyList<string> IncomeParameters = [IncomeFree, IncomeBonusShipment];

    [GeneratedRegex("^[A-Z][a-z]+[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex EventKeyPattern();

    /// <summary>The event key when <paramref name="value"/> is a well-formed key string, else null.</summary>
    public static string? ReadEventKey(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } key && EventKeyPattern().IsMatch(key)
            ? key
            : null;

    /// <summary>The income when <paramref name="value"/> is a non-negative JSON integer, else null.</summary>
    public static int? ReadIncome(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var income) && income >= 0 ? income : null;

    /// <summary>The week number when <paramref name="value"/> is a positive JSON integer, else null.</summary>
    public static int? ReadWeekNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var week) && week >= 1 ? week : null;
}
