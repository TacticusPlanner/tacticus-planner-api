using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>A validation failure named after the request field it concerns, so an endpoint can turn it
/// into a 400 that points at that field (design D7).</summary>
public sealed record LegendaryEventValidationFailure(string Field, string Message);

/// <summary>
/// Every team write is checked against the <em>current</em> catalog (design D2/D7): the lane exists, every
/// member and reserve unit is allowed on that lane and appears once, every objective index is one of the
/// lane's and appears once, the run is 1–3, the depth fits the lane's battle count and travels with its
/// source, the name is 1–60 characters after trimming. Pure over the catalog snapshot, so it is unit-tested
/// directly.
/// </summary>
public sealed class LegendaryEventCatalogValidator(IGameCatalogProvider catalog)
{
    public GameCatalogLreView? FindEvent(string? eventId) => catalog.Current.FindLre(eventId);

    /// <summary>The lane of <paramref name="lre"/> keyed <paramref name="laneId"/>, or null (reported by
    /// <see cref="ValidateTeam"/> as a <c>laneId</c> failure).</summary>
    public static GameCatalogLreTrackView? FindLane(GameCatalogLreView lre, string? laneId) => lre.Lane(laneId);

    public static IReadOnlyList<LegendaryEventValidationFailure> ValidateNotes(string? notes) =>
        notes is { Length: > LegendaryEventPlanRules.MaxNotesLength }
            ? [new("notes", $"Notes must be at most {LegendaryEventPlanRules.MaxNotesLength} characters.")]
            : [];

    public static IReadOnlyList<LegendaryEventValidationFailure> ValidateTeam(
        GameCatalogLreView lre,
        string? laneId,
        string? name,
        IReadOnlyList<string>? memberUnitIds,
        string? reserveUnitId,
        IReadOnlyList<int>? objectiveIndexes,
        LegendaryEventRunDepthWrite? depth)
    {
        var failures = new List<LegendaryEventValidationFailure>();
        var lane = FindLane(lre, laneId);
        if (lane is null)
        {
            failures.Add(new("laneId", $"The lane must be one of {string.Join(", ", LegendaryEventPlanRules.LaneIds)}."));
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > LegendaryEventPlanRules.MaxTeamNameLength)
        {
            failures.Add(new("name", $"The team name must be 1 to {LegendaryEventPlanRules.MaxTeamNameLength} characters."));
        }

        var members = memberUnitIds ?? [];
        if (members.Count is 0 or > LegendaryEventPlanRules.MaxTeamSize)
        {
            failures.Add(new("memberUnitIds", $"A team has 1 to {LegendaryEventPlanRules.MaxTeamSize} members."));
        }

        if (lane is not null)
        {
            var allowed = lane.AvailableUnitIds.ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unitId in members)
            {
                if (string.IsNullOrWhiteSpace(unitId) || !allowed.Contains(unitId))
                {
                    failures.Add(new("memberUnitIds", $"Unit '{unitId}' is not allowed on the {laneId} lane."));
                }
                else if (!seen.Add(unitId))
                {
                    failures.Add(new("memberUnitIds", $"Unit '{unitId}' appears more than once in the team."));
                }
            }

            if (reserveUnitId is not null)
            {
                if (!allowed.Contains(reserveUnitId))
                {
                    failures.Add(new("reserveUnitId", $"Unit '{reserveUnitId}' is not allowed on the {laneId} lane."));
                }
                else if (!seen.Add(reserveUnitId))
                {
                    failures.Add(new("reserveUnitId", $"Unit '{reserveUnitId}' is already a member of the team."));
                }
            }

            var indexes = lane.UnitsRestrictions.Select(objective => objective.Index).ToHashSet();
            var seenIndexes = new HashSet<int>();
            foreach (var index in objectiveIndexes ?? [])
            {
                if (!indexes.Contains(index))
                {
                    failures.Add(new("objectiveIndexes", $"Objective index {index} is not on the {laneId} lane."));
                }
                else if (!seenIndexes.Add(index))
                {
                    failures.Add(new("objectiveIndexes", $"Objective index {index} appears more than once."));
                }
            }
        }

        if (depth is not null)
        {
            failures.AddRange(ValidateDepth(lane, depth));
        }

        return failures;
    }

    public static IReadOnlyList<LegendaryEventValidationFailure> ValidateDepth(
        GameCatalogLreTrackView? lane, LegendaryEventRunDepthWrite depth)
    {
        var failures = new List<LegendaryEventValidationFailure>();
        if (depth.Run is < LegendaryEventPlanRules.MinRun or > LegendaryEventPlanRules.MaxRun)
        {
            failures.Add(new("run", $"The run must be between {LegendaryEventPlanRules.MinRun} and {LegendaryEventPlanRules.MaxRun}."));
        }

        if (depth.ExpectedBattleClears is { } clears)
        {
            var battleCount = lane?.BattleIds.Count ?? int.MaxValue;
            if (clears < 1 || clears > battleCount)
            {
                failures.Add(new("expectedBattleClears", $"The clear depth must be between 1 and the lane's {battleCount} battles."));
            }

            if (depth.Source is null)
            {
                failures.Add(new("expectedBattleClearsSource", "A clear depth needs a source (estimate or manual)."));
            }
        }
        else if (depth.Source is not null)
        {
            failures.Add(new("expectedBattleClearsSource", "A source without a clear depth is not allowed."));
        }

        return failures;
    }
}
