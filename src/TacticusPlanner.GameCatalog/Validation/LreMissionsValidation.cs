using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    /// <summary>Every raw LRE <c>regularMissions</c> / <c>premiumMissions</c> line parses into a typed mission
    /// (<see cref="GameCatalogLreMission"/>): it matches one of <c>LreDenormalizer</c>'s templates and its faction,
    /// alliance, damage type or trait label is in that template's alias table. Each failure names the event, the
    /// source file, the list, the line's index and the line.</summary>
    internal static void ValidateLreMissions(IEnumerable<GameCatalogLre> lres, List<GameCatalogValidationError> errors)
    {
        foreach (var lre in lres)
        {
            ValidateLreMissionList(lre, "regularMissions", lre.RegularMissions, errors);
            ValidateLreMissionList(lre, "premiumMissions", lre.PremiumMissions, errors);
        }
    }

    private static void ValidateLreMissionList(
        GameCatalogLre lre, string listName, IReadOnlyList<string>? lines, List<GameCatalogValidationError> errors)
    {
        var missions = lines ?? [];
        for (var index = 0; index < missions.Count; index++)
        {
            if (GameCatalogDenormalizer.ParseLreMission(index, missions[index], out var problem) is not null)
                continue;

            errors.Add(new GameCatalogValidationError(
                GameCatalogDatasets.LresPrefix,
                "UnparsedMission",
                $"LRE '{lre.UnitSnowprintId}' ({lre.SourceFile}) {listName}[{index}] '{missions[index]}': {problem}."));
        }
    }
}
