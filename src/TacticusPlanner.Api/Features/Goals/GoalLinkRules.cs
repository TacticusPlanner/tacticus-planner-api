using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>Rules for an Upgrade goal's <see cref="Goal.LinkedGoalIds"/>: only an Upgrade goal links, and each
/// link names a distinct Active or Paused Rank (Character) or Ability (Machine of War) goal of the same unit.</summary>
internal static class GoalLinkRules
{
    public const int MaxLinks = 10;

    public const string ShapeMessage = "LinkedGoalIds must be distinct, non-empty goal ids (at most 10).";

    public static bool HasValidShape(List<Guid>? ids) =>
        ids is null || (ids.Count <= MaxLinks && ids.TrueForAll(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count);

    /// <summary>The goal type a link from <paramref name="entityType"/>'s Upgrade goal may name.</summary>
    public static GoalType LinkableType(GoalEntityType entityType) =>
        entityType == GoalEntityType.Mow ? GoalType.Ability : GoalType.Rank;

    /// <summary>Null when <paramref name="ids"/> are valid links for an Upgrade goal on the unit, else the error.
    /// <paramref name="inFlight"/> is the profile's Active/Paused goals by id.</summary>
    public static string? Error(
        GoalType goalType,
        GoalEntityType entityType,
        string entityId,
        IReadOnlyCollection<Guid> ids,
        IReadOnlyDictionary<Guid, Goal> inFlight)
    {
        if (ids.Count == 0)
            return null;
        if (goalType != GoalType.Upgrade)
            return "Only an Upgrade goal can link to other goals.";
        if (!HasValidShape(ids.ToList()))
            return ShapeMessage;
        var linkable = LinkableType(entityType);
        return ids.All(id => inFlight.TryGetValue(id, out var linked)
                && linked.EntityType == entityType
                && linked.EntityId == entityId
                && linked.GoalType == linkable)
            ? null
            : entityType == GoalEntityType.Mow
                ? "LinkedGoalIds must reference active or paused Ability goals of the same Machine of War."
                : "LinkedGoalIds must reference active or paused Rank goals of the same character.";
    }
}
