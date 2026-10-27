using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateHres(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateHres(snapshot.HreViews, snapshot.CharacterViews, snapshot.FactionViews, snapshot.DamageTypeViews, errors);

    /// <summary>
    /// Each Hero Release Event's hero (and any mission task's <c>heroId</c>) is a served character, its rarities are
    /// known, its featured faction (and any task's <c>factionId</c>) a served faction, any task's <c>factionTrait</c> an
    /// alliance and any task's <c>damageProfileType</c> a served damage type. Each failure is an <c>InvalidHre</c> error.
    /// </summary>
    internal static void ValidateHres(
        IReadOnlyList<GameCatalogHre> hres,
        IReadOnlyList<GameCatalogCharacterView> characters,
        IReadOnlyList<GameCatalogFaction> factions,
        IReadOnlyList<GameCatalogDamageType> damageTypes,
        List<GameCatalogValidationError> errors)
    {
        ValidateUniqueIds(GameCatalogDatasets.Hre, hres, hre => hre.Id, errors);
        var characterIds = characters.Select(character => character.Id).ToHashSet(StringComparer.Ordinal);
        var factionIds = factions.Select(faction => faction.Id).ToHashSet(StringComparer.Ordinal);
        var alliances = factions.Select(faction => faction.Alliance).ToHashSet(StringComparer.Ordinal);
        var damageTypeIds = damageTypes.Select(damageType => damageType.Id).ToHashSet(StringComparer.Ordinal);

        void Fail(GameCatalogHre hre, string message) =>
            errors.Add(new GameCatalogValidationError(GameCatalogDatasets.Hre, "InvalidHre", $"Hero Release Event '{hre.Id}' {message}"));

        foreach (var hre in hres)
        {
            if (!characterIds.Contains(hre.HeroId))
            {
                Fail(hre, $"unlocks '{hre.HeroId}', which is not a served character.");
            }

            if (!ValidRarities.Contains(hre.BaseRarity) || !ValidRarities.Contains(hre.MaxRarity))
            {
                Fail(hre, $"has rarities '{hre.BaseRarity}'-'{hre.MaxRarity}'.");
            }

            if (!factionIds.Contains(hre.FeaturedFaction))
            {
                Fail(hre, $"features faction '{hre.FeaturedFaction}', which is not a served faction.");
            }

            foreach (var (mission, task) in hre.Missions.SelectMany(mission => mission.Tasks.Select(task => (mission, task))))
            {
                foreach (var (key, value) in task.Parameters)
                {
                    var known = key switch
                    {
                        "heroId" => characterIds.Contains(value),
                        "factionId" => factionIds.Contains(value),
                        "factionTrait" => alliances.Contains(value),
                        "damageProfileType" => damageTypeIds.Contains(value),
                        _ => true,
                    };
                    if (!known)
                    {
                        Fail(hre, $"mission '{mission.Id}' task '{task.Name}' has unknown {key} '{value}'.");
                    }
                }
            }
        }
    }
}
