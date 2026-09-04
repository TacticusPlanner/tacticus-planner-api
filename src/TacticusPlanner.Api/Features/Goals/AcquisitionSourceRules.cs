using System.Text.RegularExpressions;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Validation for <see cref="CreateGoalConfigRequest.AcquisitionSources"/>, split the same way the rest of
/// the goal validation is: <see cref="ShapeError"/> is catalog-free request-shape checking (used by the
/// FluentValidation validators), <see cref="SemanticError"/> needs the game catalog / target unit (used by
/// <see cref="GoalTargetValidationService"/>).
/// </summary>
public static partial class AcquisitionSourceRules
{
    // <shopId>:<rewardType> where rewardType is a character-/mythic-shard reward id.
    [GeneratedRegex(@"^[A-Za-z0-9_-]+:(?:shards|mythicShards)_[A-Za-z0-9]+$")]
    private static partial Regex ShopOfferIdRegex();

    /// <summary>Request-shape only: known <c>kind</c>, empty <c>ids</c> for run-based kinds, and a
    /// well-formed <c>&lt;shopId&gt;:&lt;rewardType&gt;</c> for every <c>Shop</c> id. Null / empty list is
    /// valid (means "unrestricted campaign farming").</summary>
    public static string? ShapeError(IReadOnlyList<AcquisitionSourceRequest>? sources)
    {
        if (sources is null || sources.Count == 0) return null;

        foreach (var source in sources)
        {
            if (!AcquisitionSourceKinds.IsKnown(source.Kind))
                return $"Unsupported acquisition source kind '{source.Kind}'.";

            var ids = source.Ids ?? [];

            if (AcquisitionSourceKinds.RunBased.Contains(source.Kind) && ids.Count > 0)
                return $"A {source.Kind} acquisition source must not carry ids.";

            if (source.Kind == AcquisitionSourceKinds.Shop
                && ids.Any(id => !ShopOfferIdRegex().IsMatch(id)))
            {
                return "A shop acquisition source id must be '<shopId>:<rewardType>'.";
            }
        }

        return null;
    }

    /// <summary>Catalog-aware checks: campaign ids belong to the character's shard nodes, shop ids name a
    /// known shop, and each kind is allowed for this entity/goal type. Assumes <see cref="ShapeError"/>
    /// already passed.</summary>
    public static string? SemanticError(
        IReadOnlyList<AcquisitionSourceRequest>? sources,
        GoalType goalType,
        GoalEntityType entityType,
        IReadOnlySet<string> regularShardBattleIds,
        IReadOnlySet<string> mythicShardBattleIds,
        IReadOnlySet<string> knownShopIds)
    {
        if (sources is null || sources.Count == 0) return null;

        if (goalType is not (GoalType.Unlock or GoalType.Ascension))
            return "Acquisition sources are only valid for Unlock and Ascension goals.";

        foreach (var source in sources)
        {
            switch (source.Kind)
            {
                case AcquisitionSourceKinds.Campaign:
                    if (source.Ids.Any(id =>
                        !regularShardBattleIds.Contains(id) && !mythicShardBattleIds.Contains(id)))
                    {
                        return "A campaign acquisition source is not a shard node for this unit.";
                    }

                    break;

                case AcquisitionSourceKinds.Onslaught:
                    if (entityType != GoalEntityType.Character || goalType != GoalType.Ascension)
                        return "Onslaught is only available for Character Ascension goals.";

                    break;

                case AcquisitionSourceKinds.Shop:
                    if (entityType != GoalEntityType.Character)
                        return "Shop acquisition sources are only available for Character goals.";

                    if (source.Ids.Any(id => !knownShopIds.Contains(id.Split(':', 2)[0])))
                        return "A shop acquisition source names an unknown shop.";

                    break;
            }
        }

        return null;
    }
}
