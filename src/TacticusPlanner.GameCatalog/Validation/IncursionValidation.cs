using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateIncursions(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateIncursions(snapshot.IncursionViews, snapshot.MowList, snapshot.NpcList, snapshot.FactionViews, errors);

    /// <summary>
    /// Each Incursion's MoW is a served MoW, its allowed factions served factions, its tier rarities known and every
    /// battle enemy a served npc. Each failure is an <c>InvalidIncursion</c> error.
    /// </summary>
    internal static void ValidateIncursions(
        IReadOnlyList<GameCatalogIncursion> incursions,
        IReadOnlyList<GameCatalogMow> mows,
        IReadOnlyList<GameCatalogNpc> npcs,
        IReadOnlyList<GameCatalogFaction> factions,
        List<GameCatalogValidationError> errors)
    {
        ValidateUniqueIds(GameCatalogDatasets.Incursions, incursions, incursion => incursion.MowId, errors);
        var mowIds = mows.Select(mow => mow.Id).ToHashSet(StringComparer.Ordinal);
        var npcIds = npcs.Select(npc => npc.Id).ToHashSet(StringComparer.Ordinal);
        var factionIds = factions.Select(faction => faction.Id).ToHashSet(StringComparer.Ordinal);

        void Fail(GameCatalogIncursion incursion, string message) =>
            errors.Add(new GameCatalogValidationError(
                GameCatalogDatasets.Incursions, "InvalidIncursion", $"Incursion '{incursion.MowId}' {message}"));

        foreach (var incursion in incursions)
        {
            if (!mowIds.Contains(incursion.MowId))
            {
                Fail(incursion, "is not for a served MoW.");
            }

            foreach (var faction in incursion.AllowedFactions.Where(faction => !factionIds.Contains(faction)))
            {
                Fail(incursion, $"allows faction '{faction}', which is not a served faction.");
            }

            foreach (var tier in incursion.Tiers)
            {
                if (!ValidRarities.Contains(tier.Rarity))
                {
                    Fail(incursion, $"tier {tier.Index} has rarity '{tier.Rarity}'.");
                }

                foreach (var npcId in tier.Battles
                             .SelectMany(battle => battle.Enemies)
                             .Select(enemy => enemy.NpcId)
                             .Distinct(StringComparer.Ordinal)
                             .Where(npcId => !npcIds.Contains(npcId)))
                {
                    Fail(incursion, $"tier {tier.Index} has enemy '{npcId}', which is not a served npc.");
                }
            }
        }
    }
}
