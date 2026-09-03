using System.Globalization;

namespace TacticusPlanner.GameCatalog.Utils;

/// <summary>
/// Pure build-time transforms for the daily-shop dataset: parsing <c>"type:qty"</c> reward / free-offer
/// strings, reducing a Quartz <c>cronSchedule</c> to an explicit day-of-week list, and the small
/// purchase-cap / shard-unit-id derivations. Shared by <c>Denormalization/ShopsDenormalizer.cs</c> (which
/// applies them) and <c>Validation/ShopsValidation.cs</c> (which fails the build when one cannot be applied),
/// and directly unit-tested.
/// </summary>
public static class ShopNormalization
{
    /// <summary>Day-of-week tokens in canonical weekday order — the shape of every reduced <c>days</c> list.</summary>
    public static readonly IReadOnlyList<string> WeekdaysInOrder = ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"];

    private static readonly string[] ShardPrefixes = ["shards_", "mythicShards_"];

    private static readonly HashSet<string> WeekdaySet = new(WeekdaysInOrder, StringComparer.Ordinal);

    /// <summary>
    /// Parses a <c>"type"</c> or <c>"type:qty"</c> reward / free-offer string. An absent quantity means 1.
    /// Fails when the type segment is empty or the quantity segment is present but not a positive integer.
    /// </summary>
    public static bool TryParseTypedQuantity(string? raw, out string type, out int qty)
    {
        type = string.Empty;
        qty = 0;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var separator = raw.IndexOf(':');
        if (separator < 0)
        {
            type = raw;
            qty = 1;
            return true;
        }

        var typeSegment = raw[..separator];
        var qtySegment = raw[(separator + 1)..];
        if (typeSegment.Length == 0
            || !int.TryParse(qtySegment, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedQty)
            || parsedQty <= 0)
        {
            return false;
        }

        type = typeSegment;
        qty = parsedQty;
        return true;
    }

    /// <summary>True when <paramref name="type"/> non-empty and <paramref name="amount"/> is a finite, non-negative number.</summary>
    public static bool IsParseableCost(string? type, double amount) =>
        !string.IsNullOrWhiteSpace(type) && double.IsFinite(amount) && amount >= 0;

    /// <summary>The stringified integer cap, defaulting to 1 when absent or non-numeric.</summary>
    public static int ParseMaxPurchasesPerDay(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 1;

    /// <summary>The unit id embedded in a <c>shards_&lt;id&gt;</c> / <c>mythicShards_&lt;id&gt;</c> reward type, or null.</summary>
    public static string? ShardUnitId(string? rewardType)
    {
        if (string.IsNullOrEmpty(rewardType))
        {
            return null;
        }

        var prefix = ShardPrefixes.FirstOrDefault(candidate =>
            rewardType.StartsWith(candidate, StringComparison.Ordinal) && rewardType.Length > candidate.Length);

        return prefix is null ? null : rewardType[prefix.Length..];
    }

    /// <summary>
    /// Reduces a Quartz <c>cronSchedule</c> to an explicit day-of-week list in weekday order. Reads only the
    /// day-of-week field (index 5); <c>*</c> or <c>?</c> there means "every day". Every current source cron is
    /// a pure day-of-week gate (<c>0 0 0 ? * &lt;DOW&gt; *</c>) — a cron that carries a genuine time-of-day or
    /// day-of-month restriction, or an unrecognized day token, yields an empty list, which
    /// <c>Validation/ShopsValidation.cs</c> then fails the build on (rather than silently mis-reducing).
    /// </summary>
    public static IReadOnlyList<string> ReduceCronToDays(string? cronSchedule)
    {
        if (string.IsNullOrWhiteSpace(cronSchedule))
        {
            return [];
        }

        var fields = cronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 6)
        {
            return [];
        }

        // Only a pure day-of-week gate reduces losslessly to a day list: seconds/minutes/hours must all be
        // 0 and the day-of-month and month fields unrestricted. A cron carrying a genuine time-of-day or
        // calendar restriction (e.g. "0 0 12 ? * MON *") is not representable as a plain day list — yield an
        // empty list so Validation/ShopsValidation.cs fails the build instead of silently dropping it.
        var timeFiresAtMidnight = fields[0] == "0" && fields[1] == "0" && fields[2] == "0";
        var dayOfMonthUnrestricted = fields[3] is "?" or "*";
        var monthUnrestricted = fields[4] is "*" or "?";
        if (!timeFiresAtMidnight || !dayOfMonthUnrestricted || !monthUnrestricted)
        {
            return [];
        }

        var dayOfWeekField = fields[5];
        if (dayOfWeekField is "*" or "?")
        {
            return WeekdaysInOrder;
        }

        var tokens = dayOfWeekField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToUpperInvariant())
            .ToArray();

        if (tokens.Length == 0 || tokens.Any(token => !WeekdaySet.Contains(token)))
        {
            return [];
        }

        return WeekdaysInOrder.Where(tokens.Contains).ToArray();
    }
}
