namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Shape rules for the optional Upgrade-goal range groups; semantic bounds live in
/// <see cref="GoalTargetValidationService"/>.</summary>
internal static class UpgradeRangeRules
{
    public const string ShapeMessage = "Each upgrade range needs both a start and an end.";

    public static bool HasWholeRanges(UpgradeTargetRequest? upgrade) =>
        upgrade is null || new[] { upgrade.RankRange, upgrade.ActiveRange, upgrade.PassiveRange }
            .All(range => range is null or { Start: not null, End: not null });
}
