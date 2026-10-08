using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>The team fields every create/update carries, validated together against the catalog.</summary>
public sealed record LegendaryEventTeamFields(
    string? Name,
    IReadOnlyList<string>? MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int>? ObjectiveIndexes,
    int Run,
    int? ExpectedBattleClears,
    string? ExpectedBattleClearsSource
);

/// <param name="Field">The request property (camelCase) the failure is reported against.</param>
public sealed record LegendaryEventFieldFailure(string Field, string Message);

/// <summary>
/// Validates plan and team writes against the current Game Catalog's <c>lres</c> views (design D2, D7): the
/// event and lane exist, units are allowed on the lane, objective indexes belong to the lane, run and depth are
/// in range, and the depth travels with its source. Reads never go through here, so a plan written under an
/// older catalog still reads.
/// </summary>
public sealed class LegendaryEventCatalogValidator(IGameCatalogProvider catalog)
{
    public GameCatalogLreView? FindEvent(string eventId) =>
        catalog.Current.LreViews.FirstOrDefault(view => view.Id == eventId);

    public static GameCatalogLreTrackView? FindLane(GameCatalogLreView lre, string? laneId) => laneId switch
    {
        "alpha" => lre.Alpha,
        "beta" => lre.Beta,
        "gamma" => lre.Gamma,
        _ => null,
    };

    public static bool TryParseSource(string? value, out LegendaryEventDepthSource source)
    {
        switch (value)
        {
            case "estimate":
                source = LegendaryEventDepthSource.Estimate;
                return true;
            case "manual":
                source = LegendaryEventDepthSource.Manual;
                return true;
            default:
                source = default;
                return false;
        }
    }

    public static IReadOnlyList<LegendaryEventFieldFailure> ValidatePlan(string? notes) =>
        notes is { Length: > LegendaryEventValidation.MaxNotesLength }
            ? [new("notes", $"Notes must be at most {LegendaryEventValidation.MaxNotesLength} characters.")]
            : [];

    public static IReadOnlyList<LegendaryEventFieldFailure> ValidateTeam(
        GameCatalogLreView lre, string? laneId, LegendaryEventTeamFields fields)
    {
        var failures = new List<LegendaryEventFieldFailure>();
        var lane = FindLane(lre, laneId);
        if (lane is null)
        {
            failures.Add(new("laneId", "laneId must be one of alpha, beta or gamma."));
            return failures;
        }

        var name = fields.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > LegendaryEventValidation.MaxTeamNameLength)
            failures.Add(new("name", $"The team name must be 1–{LegendaryEventValidation.MaxTeamNameLength} characters."));

        ValidateUnits(lane, fields, failures);
        ValidateObjectives(lane, fields.ObjectiveIndexes, failures);

        if (fields.Run is < LegendaryEventValidation.MinRun or > LegendaryEventValidation.MaxRun)
            failures.Add(new("run", $"run must be {LegendaryEventValidation.MinRun}–{LegendaryEventValidation.MaxRun}."));

        var battles = lane.BattleIds.Count;
        if (fields.ExpectedBattleClears is { } depth && (depth < 1 || depth > battles))
            failures.Add(new("expectedBattleClears", $"expectedBattleClears must be 1–{battles} for this lane."));

        var hasSource = fields.ExpectedBattleClearsSource is not null;
        if (hasSource && !TryParseSource(fields.ExpectedBattleClearsSource, out _))
            failures.Add(new("expectedBattleClearsSource", "expectedBattleClearsSource must be estimate or manual."));
        else if (fields.ExpectedBattleClears is not null && !hasSource)
            failures.Add(new("expectedBattleClearsSource", "A clear depth needs its source (estimate or manual)."));
        else if (fields.ExpectedBattleClears is null && hasSource)
            failures.Add(new("expectedBattleClearsSource", "expectedBattleClearsSource must be null when there is no clear depth."));

        return failures;
    }

    private static void ValidateUnits(
        GameCatalogLreTrackView lane, LegendaryEventTeamFields fields, List<LegendaryEventFieldFailure> failures)
    {
        var allowed = lane.AvailableUnitIds.ToHashSet(StringComparer.Ordinal);
        var members = fields.MemberUnitIds ?? [];
        if (members.Count is 0 or > LegendaryEventValidation.MaxMembers)
        {
            failures.Add(new("memberUnitIds", $"A team has 1–{LegendaryEventValidation.MaxMembers} members."));
        }
        else if (members.Any(unitId => unitId is null || !allowed.Contains(unitId)))
        {
            failures.Add(new("memberUnitIds", "Every member must be a unit allowed on this lane."));
        }
        else if (members.Distinct(StringComparer.Ordinal).Count() != members.Count)
        {
            failures.Add(new("memberUnitIds", "A unit can appear only once in a team."));
        }

        if (fields.ReserveUnitId is not { } reserve)
            return;
        if (!allowed.Contains(reserve))
            failures.Add(new("reserveUnitId", "The reserve must be a unit allowed on this lane."));
        else if (members.Contains(reserve, StringComparer.Ordinal))
            failures.Add(new("reserveUnitId", "The reserve cannot also be a member of the team."));
    }

    private static void ValidateObjectives(
        GameCatalogLreTrackView lane, IReadOnlyList<int>? indexes, List<LegendaryEventFieldFailure> failures)
    {
        var objectives = indexes ?? [];
        var known = lane.UnitsRestrictions.Select(restriction => restriction.Index).ToHashSet();
        if (objectives.Any(index => !known.Contains(index)))
            failures.Add(new("objectiveIndexes", "Every objective index must be an objective of this lane."));
        else if (objectives.Distinct().Count() != objectives.Count)
            failures.Add(new("objectiveIndexes", "An objective can be listed only once."));
    }
}
