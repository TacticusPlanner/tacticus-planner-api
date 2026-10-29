using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>A Legendary Event lane on the wire; the domain stores its <see cref="LegendaryEventValidation.LaneIds"/>
/// key, in the same order.</summary>
public enum LegendaryEventLaneId
{
    [JsonStringEnumMemberName("alpha")] Alpha,
    [JsonStringEnumMemberName("beta")] Beta,
    [JsonStringEnumMemberName("gamma")] Gamma,
}

/// <summary>Shape limits for Legendary Event plans and teams (Stage 2 design D6, D7, D12; Stage 3 run inputs and annotations).</summary>
public static class LegendaryEventValidation
{
    public static readonly IReadOnlyList<string> LaneIds = ["alpha", "beta", "gamma"];

    public static string LaneKey(LegendaryEventLaneId lane) => LaneIds[(int)lane];

    /// <summary>Inverse of <see cref="LaneKey"/> for a stored, already-validated key.</summary>
    public static LegendaryEventLaneId Lane(string laneId) =>
        LaneIds.ToList().IndexOf(laneId) is >= 0 and var index
            ? (LegendaryEventLaneId)index
            : throw new ArgumentOutOfRangeException(nameof(laneId), laneId, "Unknown lane.");

    public const int MaxEventIdLength = 128;

    public const int MaxCatalogVersionLength = 64;

    public const int MaxNotesLength = 2000;

    public const int MaxTeamNameLength = 60;

    public const int MaxUnitIdLength = 128;

    /// <summary>The fewest non-reserve members a team can field in the game; the reserve does not count.</summary>
    public const int MinMembers = 3;

    public const int MaxMembers = 5;

    /// <summary>The most teams on one lane: a lane has five objectives, so a plan rarely needs more than a dozen
    /// line-ups per lane.</summary>
    public const int MaxTeamsPerLane = 50;

    public const int MinRun = 1;

    public const int MaxRun = 3;

    /// <summary>V1's bound for "Oh So Close" shards per run: three chest-sized awards.</summary>
    public const int MaxCloseShards = 75;

    /// <summary>Objective ids of a battle cell: 0 is defeat-all, 1..5 the lane objectives.</summary>
    public const int MaxObjectiveId = 5;

    public const string MaybeClearStatus = "maybeClear";

    public const string StopHereStatus = "stopHere";

    /// <summary>
    /// The plan shape invariants that used to be table constraints: lane ids, line-up size, the single reserve,
    /// unique unit per team, dense unique <c>(lane, sortOrder)</c>, objective indexes, run range, depth and one
    /// depth per run, and the run/annotation ranges. Request validation rejects bad input with a 400 first, so a
    /// violation here is a bug: it throws instead of saving.
    /// </summary>
    public static void AssertInvariants(LegendaryEventPlan plan)
    {
        foreach (var lane in plan.Teams.GroupBy(team => team.LaneId))
        {
            Check(LaneIds.Contains(lane.Key), $"unknown lane '{lane.Key}'");
            Check(lane.Select(team => team.SortOrder).Order().SequenceEqual(Enumerable.Range(0, lane.Count())),
                $"lane '{lane.Key}' sort orders are not dense and unique");
        }

        Check(plan.Teams.Select(team => team.Id).Distinct().Count() == plan.Teams.Count, "duplicate team id");
        foreach (var team in plan.Teams)
        {
            Check(team.MemberUnitIds.Count <= MaxMembers, $"team {team.Id} has more than {MaxMembers} members");
            var units = team.MemberUnitIds.Append(team.ReserveUnitId).OfType<string>().ToList();
            Check(units.Distinct(StringComparer.Ordinal).Count() == units.Count, $"team {team.Id} repeats a unit");
            Check(team.ObjectiveIndexes.All(index => index >= 0)
                && team.ObjectiveIndexes.Distinct().Count() == team.ObjectiveIndexes.Count,
                $"team {team.Id} has invalid objective indexes");
            Check(team.RunDepths.All(depth => depth.Run is >= MinRun and <= MaxRun && depth.ExpectedBattleClears >= 1)
                && team.RunDepths.Select(depth => depth.Run).Distinct().Count() == team.RunDepths.Count,
                $"team {team.Id} has an invalid run depth");
        }

        Check(plan.Runs.All(run => run is { Run: >= MinRun and <= MaxRun, RegularMissions: >= 0, PremiumMissions: >= 0 }
            && run.CloseShards is >= 0 and <= MaxCloseShards)
            && plan.Runs.Select(run => run.Run).Distinct().Count() == plan.Runs.Count,
            "invalid run inputs");
        Check(plan.Annotations.All(annotation => LaneIds.Contains(annotation.LaneId)
            && annotation is { BattleIndex: >= 0, ObjectiveId: >= 0 and <= MaxObjectiveId })
            && plan.Annotations.Select(a => (a.LaneId, a.BattleIndex, a.ObjectiveId)).Distinct().Count()
                == plan.Annotations.Count,
            "invalid annotations");
    }

    private static void Check(bool condition, string violation)
    {
        if (!condition)
            throw new InvalidOperationException($"Legendary Event plan invariant violated: {violation}.");
    }
}
