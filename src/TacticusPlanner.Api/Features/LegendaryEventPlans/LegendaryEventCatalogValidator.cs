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
    public static readonly LegendaryEventFieldFailure UnknownLane =
        new("laneId", $"laneId must be one of {string.Join(", ", LegendaryEventValidation.LaneIds)}.");

    public GameCatalogLreView? FindEvent(string eventId) =>
        catalog.Current.LreViews.FirstOrDefault(view => view.Id == eventId);

    public static GameCatalogLreTrackView? FindLane(GameCatalogLreView lre, string? laneId) => lre.Lane(laneId);

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
            failures.Add(UnknownLane);
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

    /// <summary>Run inputs (Stage 3 design D3): the route's run is 1–3, missions are within the event's catalog
    /// mission counts, completed missions are distinct catalog indices no more numerous than their count
    /// (add-legendary-event-mission-tracking design D2) and close shards within 0–75. Paid fields are validated whatever showPaidOptions says.</summary>
    public static IReadOnlyList<LegendaryEventFieldFailure> ValidateRunInputs(
        GameCatalogLreView lre, string? run, UpdateLegendaryEventRunInputsRequest request)
    {
        var failures = new List<LegendaryEventFieldFailure>();
        if (ParseRun(run) is null)
            failures.Add(new("run", $"run must be {LegendaryEventValidation.MinRun}–{LegendaryEventValidation.MaxRun}."));

        ValidateMissions(
            "regularMissions", request.RegularMissions, lre.RegularMissions.Count,
            "completedRegularMissions", request.CompletedRegularMissions, failures);
        ValidateMissions(
            "premiumMissions", request.PremiumMissions, lre.PremiumMissions.Count,
            "completedPremiumMissions", request.CompletedPremiumMissions, failures);

        if (request.CloseShards is < 0 or > LegendaryEventValidation.MaxCloseShards)
            failures.Add(new("closeShards", $"closeShards must be 0–{LegendaryEventValidation.MaxCloseShards}."));

        return failures;
    }

    private static void ValidateMissions(
        string countField,
        int count,
        int catalogCount,
        string completedField,
        IReadOnlyList<int>? completed,
        List<LegendaryEventFieldFailure> failures)
    {
        if (count < 0 || count > catalogCount)
            failures.Add(new(countField, $"{countField} must be 0–{catalogCount} for this event."));

        var indices = completed ?? [];
        if (indices.Any(index => index < 0 || index >= catalogCount))
            failures.Add(new(completedField, $"Every {completedField} index must be 0–{catalogCount - 1} for this event."));
        else if (indices.Distinct().Count() != indices.Count)
            failures.Add(new(completedField, $"A mission can be listed in {completedField} only once."));
        else if (count >= 0 && count <= catalogCount && count < indices.Count)
            failures.Add(new(countField, $"{countField} must be at least the {indices.Count} missions in {completedField}."));
    }

    /// <summary>The route's run as 1–3, or null when it is not a number in range.</summary>
    public static int? ParseRun(string? run) =>
        int.TryParse(run, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value is >= LegendaryEventValidation.MinRun and <= LegendaryEventValidation.MaxRun
            ? value
            : null;

    /// <summary>An annotation write (Stage 3 design D6): the lane exists, the battle is on it, the cells are 1–6
    /// distinct objective ids of that lane (0 is defeat-all) and the status is maybeClear, stopHere or null.</summary>
    public static IReadOnlyList<LegendaryEventFieldFailure> ValidateAnnotations(
        GameCatalogLreView lre, UpdateLegendaryEventAnnotationsRequest request)
    {
        var failures = new List<LegendaryEventFieldFailure>();
        var lane = FindLane(lre, request.LaneId);
        if (lane is null)
        {
            failures.Add(UnknownLane);
        }
        else
        {
            var battles = lane.BattleIds.Count;
            if (request.BattleIndex < 0 || request.BattleIndex >= battles)
                failures.Add(new("battleIndex", $"battleIndex must be 0–{battles - 1} for this lane."));

            var objectiveIds = request.ObjectiveIds ?? [];
            var maxObjectiveId = lane.UnitsRestrictions.Count;
            if (objectiveIds.Count is 0 or > LegendaryEventValidation.MaxObjectiveId + 1)
                failures.Add(new("objectiveIds", $"objectiveIds must list 1–{LegendaryEventValidation.MaxObjectiveId + 1} cells."));
            else if (objectiveIds.Any(id => id < 0 || id > maxObjectiveId))
                failures.Add(new("objectiveIds", $"Every objective id must be 0–{maxObjectiveId} for this lane."));
            else if (objectiveIds.Distinct().Count() != objectiveIds.Count)
                failures.Add(new("objectiveIds", "An objective id can be listed only once."));
        }

        if (request.Status is not null && !TryParseStatus(request.Status, out _))
        {
            failures.Add(new("status",
                $"status must be {LegendaryEventValidation.MaybeClearStatus}, {LegendaryEventValidation.StopHereStatus} or null."));
        }

        return failures;
    }

    public static bool TryParseStatus(string? value, out LegendaryEventAnnotationStatus status)
    {
        switch (value)
        {
            case LegendaryEventValidation.MaybeClearStatus:
                status = LegendaryEventAnnotationStatus.MaybeClear;
                return true;
            case LegendaryEventValidation.StopHereStatus:
                status = LegendaryEventAnnotationStatus.StopHere;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static void ValidateUnits(
        GameCatalogLreTrackView lane, LegendaryEventTeamFields fields, List<LegendaryEventFieldFailure> failures)
    {
        var allowed = lane.AvailableUnitIds.ToHashSet(StringComparer.Ordinal);
        var members = fields.MemberUnitIds ?? [];
        if (members.Count is < LegendaryEventValidation.MinMembers or > LegendaryEventValidation.MaxMembers)
        {
            failures.Add(new("memberUnitIds",
                $"A team has {LegendaryEventValidation.MinMembers}–{LegendaryEventValidation.MaxMembers} members."));
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
