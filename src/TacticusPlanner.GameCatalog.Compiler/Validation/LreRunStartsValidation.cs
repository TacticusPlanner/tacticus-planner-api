using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    /// <summary>An event has at most this many runs.</summary>
    internal const int MaxLreRuns = 3;

    /// <summary>A run lasts 7 days; consecutive run starts closer than this would overlap.</summary>
    internal static readonly TimeSpan LreRunLength = TimeSpan.FromDays(7);

    /// <summary>Each raw LRE file's <c>runStartDatesUtc</c>, served as <c>lres[].eventStageStartDatesUtc</c>
    /// (index = run - 1): at most three entries, each a UTC instant, strictly ascending and at least 7 days
    /// apart. Absent or empty is valid (no announced run).</summary>
    internal static void ValidateLreRunStarts(IEnumerable<GameCatalogLre> lres, List<GameCatalogValidationError> errors)
    {
        foreach (var lre in lres)
        {
            var problem = FindRunStartsProblem(lre.RunStartDatesUtc);
            if (problem is not null)
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.LresPrefix,
                    "InvalidRunStartDates",
                    $"LRE '{lre.UnitSnowprintId}' ({lre.SourceFile}) has invalid runStartDatesUtc: {problem}."));
            }
        }
    }

    private static string? FindRunStartsProblem(IReadOnlyList<string> starts)
    {
        if (starts.Count > MaxLreRuns)
            return $"more than {MaxLreRuns} run starts ({starts.Count})";

        DateTimeOffset? previous = null;
        for (var index = 0; index < starts.Count; index++)
        {
            if (GameCatalogDenormalizer.ParseLreRunStart(starts[index]) is not { } current)
                return $"entry at index {index} ('{starts[index]}') is not a UTC date";
            if (previous is { } earlier)
            {
                if (current <= earlier)
                    return $"entry at index {index} (run {index + 1}) does not start after run {index}; run starts must be strictly ascending";
                if (current - earlier < LreRunLength)
                    return $"entry at index {index}: run {index + 1} starts {(current - earlier).TotalDays:0.##} days after run {index}, overlapping run {index} (runs last {LreRunLength.TotalDays:0} days)";
            }

            previous = current;
        }

        return null;
    }
}
