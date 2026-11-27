using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    /// <summary>Each raw LRE lane's <c>killPoints</c> is its defeat-all base: it must equal the lane's first
    /// <c>defeatAll</c> value, and <c>defeatAll</c> must not be empty. Catches a lane copied from another
    /// event's file (Uthar once carried Farsight's 34 / 26 / 17).</summary>
    internal static void ValidateLreLanePoints(IEnumerable<GameCatalogLre> lres, List<GameCatalogValidationError> errors)
    {
        foreach (var lre in lres)
        {
            foreach (var (laneName, lane) in LreLanes(lre))
            {
                var problem = lane.DefeatAll is not { Count: > 0 } defeatAll
                    ? "defeatAll is empty"
                    : lane.KillPoints != defeatAll[0]
                        ? $"killPoints {lane.KillPoints} differs from defeatAll[0] {defeatAll[0]}"
                        : null;
                if (problem is not null)
                {
                    errors.Add(new GameCatalogValidationError(
                        GameCatalogDatasets.LresPrefix,
                        "InvalidLaneKillPoints",
                        $"LRE '{lre.UnitSnowprintId}' ({lre.SourceFile}) lane {laneName} ('{lane.Name}'): {problem}."));
                }
            }
        }
    }

    private static IEnumerable<(string LaneName, GameCatalogLreTrack Lane)> LreLanes(GameCatalogLre lre)
    {
        yield return ("Alpha", lre.Alpha);
        yield return ("Beta", lre.Beta);
        yield return ("Gamma", lre.Gamma);
    }
}
