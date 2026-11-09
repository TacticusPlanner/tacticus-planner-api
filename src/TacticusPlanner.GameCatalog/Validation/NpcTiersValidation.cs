using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateNpcTiers(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var npcIds = new HashSet<string>(snapshot.NpcList.Select(npc => npc.Id), StringComparer.Ordinal);
        var bossIds = new HashSet<string>(
            snapshot.NpcList.Where(npc => npc.Traits.Contains(BossTrait, StringComparer.Ordinal)).Select(npc => npc.Id),
            StringComparer.Ordinal);
        ValidateNpcTiers(snapshot.NpcTiersViews, npcIds, bossIds, errors);
    }

    /// <summary>The NPC trait that marks a named boss (Azrael, Gulgortz, ...): bosses are not tiered.</summary>
    private const string BossTrait = "Hero";

    /// <summary>
    /// Validates the <c>npc-tiers</c> file: exactly one record with the dataset's id; <c>tiers</c> lists S, A,
    /// B, C and D in that order, each with a <c>multiplier</c> in (0, 5]; every NPC's <c>npcId</c> is a catalog
    /// NPC id listed once, not a boss (an NPC with the <c>Hero</c> trait), with one of those tiers and, when set, its own <c>multiplier</c> in (0, 5].
    /// Each failure is an <c>InvalidNpcTiers</c> error naming the offending value.
    /// </summary>
    internal static void ValidateNpcTiers(
        IReadOnlyList<GameCatalogNpcTiers> records,
        HashSet<string> npcIds,
        HashSet<string> bossIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.NpcTiers;
        const string code = "InvalidNpcTiers";

        if (records.Count != 1)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' must contain exactly one record, found {records.Count}."));
            return;
        }

        var record = records[0];

        if (record.Id != dataset)
        {
            errors.Add(new GameCatalogValidationError(
                dataset, code, $"Dataset '{dataset}' record id must be '{dataset}', found '{record.Id}'."));
        }

        var tiers = record.Tiers ?? [];
        var tierNames = tiers.Select(tier => tier.Tier).ToArray();
        if (!tierNames.SequenceEqual(HordeModeTierNames, StringComparer.Ordinal))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                code,
                $"Dataset '{dataset}' tiers '{string.Join(", ", tierNames)}' must be '{string.Join(", ", HordeModeTierNames)}' in that order."));
        }

        foreach (var tier in tiers.Where(tier => !IsHordeModeMultiplier(tier.Multiplier)))
        {
            errors.Add(new GameCatalogValidationError(
                dataset,
                code,
                $"Dataset '{dataset}' tier '{tier.Tier}' multiplier '{FormatCatalogNumber(tier.Multiplier)}' must be in (0, {FormatCatalogNumber(MaxHordeModeMultiplier)}]."));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in record.Npcs ?? [])
        {
            if (string.IsNullOrWhiteSpace(npc.NpcId) || !npcIds.Contains(npc.NpcId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' npcId '{npc.NpcId}' is not a catalog NPC id."));
            }
            else if (bossIds.Contains(npc.NpcId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' npcId '{npc.NpcId}' is a boss; only regular NPCs are tiered."));
            }
            else if (!seen.Add(npc.NpcId))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, code, $"Dataset '{dataset}' npcId '{npc.NpcId}' is listed more than once."));
            }

            if (!HordeModeTierNames.Contains(npc.Tier, StringComparer.Ordinal))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    code,
                    $"Dataset '{dataset}' npcId '{npc.NpcId}' tier '{npc.Tier}' must be one of {string.Join(", ", HordeModeTierNames)}."));
            }

            if (npc.Multiplier is { } multiplier && !IsHordeModeMultiplier(multiplier))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset,
                    code,
                    $"Dataset '{dataset}' npcId '{npc.NpcId}' multiplier '{FormatCatalogNumber(multiplier)}' must be in (0, {FormatCatalogNumber(MaxHordeModeMultiplier)}]."));
            }
        }
    }
}
