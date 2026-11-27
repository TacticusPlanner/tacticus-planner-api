using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    // A MoW's mythic ability is optional, but when authored it must name a third ability: an empty name or a
    // repeat of the primary/secondary ability is a porting mistake.
    internal static void ValidateMowMythicAbilities(IReadOnlyList<GameCatalogMow> mows, List<GameCatalogValidationError> errors)
    {
        foreach (var mow in mows)
        {
            if (mow.MythicAbility is not { } mythic)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(mythic.Name))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.Mows, "InvalidMythicAbility", $"MoW '{mow.Id}' has an empty mythicAbility name."));
            }
            else if (string.Equals(mythic.Name, mow.PrimaryAbility.Name, StringComparison.Ordinal)
                || string.Equals(mythic.Name, mow.SecondaryAbility.Name, StringComparison.Ordinal))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.Mows,
                    "InvalidMythicAbility",
                    $"MoW '{mow.Id}' mythicAbility '{mythic.Name}' repeats its primary or secondary ability."));
            }
        }
    }
}
