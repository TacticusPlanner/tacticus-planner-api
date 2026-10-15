using System.Globalization;
using System.Text.Json;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>A V1 run's inputs, clamped to the catalog and ready to write.</summary>
public sealed record V1ImportedRunInputs(int Run, int RegularMissions, int PremiumMissions, bool BundlePurchased, int CloseShards);

/// <summary>A V1 Maybe clear / Stop here mark resolved onto a V2 objective cell.</summary>
public sealed record V1ImportedAnnotation(string LaneId, int BattleIndex, int ObjectiveId, LegendaryEventAnnotationStatus Status);

/// <summary>
/// Resolves the V1 progress of one event into V2 run inputs and objective annotations (Stage 3 design D7). V2
/// values always win: a run or a cell already stored in V2 is kept and reported, never overwritten.
/// </summary>
public static class V1LegendaryEventProgressImport
{
    private const int MaybeClear = 2;
    private const int StopHere = 3;
    private const int BlockedState = 2;

    /// <summary>V1 requirement ids that are not lane objectives: defeat-all and the kill-score and high-score
    /// pseudo-objectives, which have no V2 cell of their own and map to defeat-all.</summary>
    private static readonly HashSet<string> DefeatAllIds = new(StringComparer.Ordinal) { "_defeatAll", "_killPoints", "_highScore" };

    public static List<V1ImportedRunInputs> ResolveRunInputs(
        GameCatalogLreView lre,
        Dictionary<string, V1LreOccurrence?>? overview,
        IReadOnlyCollection<int> existingRuns,
        List<V1LegendaryEventIssue> issues)
    {
        var resolved = new List<V1ImportedRunInputs>();
        foreach (var (key, entry) in (overview ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var run = LegendaryEventCatalogValidator.ParseRun(key);
            if (run is null || key.Length != 1)
            {
                issues.Add(new("unknown_run", null, key));
                continue;
            }

            if (entry is null)
                continue;

            var inputs = new V1ImportedRunInputs(
                run.Value,
                Clamp(entry.RegularMissions, lre.RegularMissions.Count, run.Value, "regularMissions", issues),
                Clamp(entry.PremiumMissions, lre.PremiumMissions.Count, run.Value, "premiumMissions", issues),
                Number(entry.Bundle) > 0,
                Clamp(entry.OhSoCloseShards, LegendaryEventValidation.MaxCloseShards, run.Value, "closeShards", issues));
            if (inputs is { RegularMissions: 0, PremiumMissions: 0, BundlePurchased: false, CloseShards: 0 })
                continue;

            if (existingRuns.Contains(inputs.Run))
            {
                issues.Add(new("existing_run_inputs_kept", null, key));
                continue;
            }

            resolved.Add(inputs);
        }

        return resolved;
    }

    public static List<V1ImportedAnnotation> ResolveAnnotations(
        GameCatalogLreView lre,
        V1LreProgress? progress,
        IReadOnlySet<(string LaneId, int BattleIndex, int ObjectiveId)> existingCells,
        List<V1LegendaryEventIssue> issues)
    {
        var marks = new List<(string LaneKey, double? BattleIndex, string? RequirementId, int Status)>();
        if (progress?.CompactProgress is { } compact)
        {
            foreach (var (laneKey, requirements) in compact)
            {
                foreach (var (requirementId, requirement) in requirements ?? [])
                {
                    // As V1 reads it: a battle with no status but a blocked state (an older save re-saved as
                    // compact) is Stop here, the same fallback as the legacy format below.
                    var statuses = ByIndex(requirement?.Statuses).ToList();
                    var withStatus = statuses.Select(entry => entry.BattleIndex).ToHashSet();
                    foreach (var (battleIndex, status) in statuses)
                        marks.Add((laneKey, battleIndex, requirementId, (int)status));
                    foreach (var (battleIndex, _) in ByIndex(requirement?.States)
                                 .Where(entry => entry.Value.Equals(BlockedState) && !withStatus.Contains(entry.BattleIndex)))
                        marks.Add((laneKey, battleIndex, requirementId, StopHere));
                }
            }
        }
        else if (progress?.BattlesProgress is { } battles)
        {
            foreach (var battle in battles.OfType<V1LreBattleProgress>())
            {
                foreach (var requirement in battle.Requirements?.OfType<V1LreRequirementProgress>() ?? [])
                {
                    var status = Number(requirement.Status) is { } explicitStatus
                        ? (int)explicitStatus
                        : Number(requirement.State) == BlockedState ? StopHere : 0;
                    marks.Add((battle.TrackId ?? string.Empty, Number(battle.BattleIndex), requirement.Id, status));
                }
            }
        }

        var cells = new Dictionary<(string LaneId, int BattleIndex, int ObjectiveId), LegendaryEventAnnotationStatus>();
        var reported = new HashSet<(string Code, string? Value)>();
        void Report(string code, string? value)
        {
            if (reported.Add((code, value)))
                issues.Add(new(code, null, value));
        }

        foreach (var (laneKey, battleIndex, requirementId, status) in marks)
        {
            if (status is not (MaybeClear or StopHere))
                continue;

            var lane = lre.Lane(laneKey);
            if (lane is null)
            {
                Report("unknown_lane", laneKey);
                continue;
            }

            if (battleIndex is not { } index || !double.IsInteger(index) || index < 0 || index >= lane.BattleIds.Count)
            {
                Report("unknown_battle", $"{laneKey}:{Format(battleIndex)}");
                continue;
            }

            var objectiveId = ObjectiveId(lane, requirementId);
            if (objectiveId is null)
            {
                Report("unknown_objective", $"{laneKey}:{requirementId}");
                continue;
            }

            var cell = (laneKey, (int)index, objectiveId.Value);
            var value = status == StopHere ? LegendaryEventAnnotationStatus.StopHere : LegendaryEventAnnotationStatus.MaybeClear;
            // Several V1 marks can land on one cell (defeat-all and the score pseudo-objectives): Stop here wins.
            if (!cells.TryGetValue(cell, out var current) || current != LegendaryEventAnnotationStatus.StopHere)
                cells[cell] = value;
        }

        var kept = cells.Keys.Count(existingCells.Contains);
        if (kept > 0)
            issues.Add(new("existing_annotations_kept", null, kept.ToString(CultureInfo.InvariantCulture)));

        return cells
            .Where(pair => !existingCells.Contains(pair.Key))
            .OrderBy(pair => LegendaryEventValidation.LaneIds.ToList().IndexOf(pair.Key.LaneId))
            .ThenBy(pair => pair.Key.BattleIndex)
            .ThenBy(pair => pair.Key.ObjectiveId)
            .Select(pair => new V1ImportedAnnotation(pair.Key.LaneId, pair.Key.BattleIndex, pair.Key.ObjectiveId, pair.Value))
            .ToList();
    }

    /// <summary>Adds the resolved rows to the plan inside the writer; a row the plan already has (written since
    /// the import read it) is left as it is.</summary>
    public static void Apply(
        LegendaryEventPlanMutationContext context,
        IReadOnlyList<V1ImportedRunInputs> runInputs,
        IReadOnlyList<V1ImportedAnnotation> annotations)
    {
        var plan = context.Plan;
        var runRows = runInputs
            .Where(inputs => plan.Runs.All(run => run.Run != inputs.Run))
            .Select(inputs => new LegendaryEventRunProgress
            {
                PlanId = plan.Id,
                Run = inputs.Run,
                RegularMissions = inputs.RegularMissions,
                PremiumMissions = inputs.PremiumMissions,
                BundlePurchased = inputs.BundlePurchased,
                CloseShards = inputs.CloseShards,
                UpdatedAt = context.Now,
            })
            .ToList();
        foreach (var row in runRows)
        {
            plan.Runs.Add(row);
            context.Db.Add(row);
        }

        var annotationRows = annotations
            .Where(annotation => !plan.Annotations.Any(existing =>
                existing.LaneId == annotation.LaneId
                && existing.BattleIndex == annotation.BattleIndex
                && existing.ObjectiveId == annotation.ObjectiveId))
            .Select(annotation => new LegendaryEventObjectiveAnnotation
            {
                PlanId = plan.Id,
                LaneId = annotation.LaneId,
                BattleIndex = annotation.BattleIndex,
                ObjectiveId = annotation.ObjectiveId,
                Status = annotation.Status,
                UpdatedAt = context.Now,
            })
            .ToList();
        foreach (var row in annotationRows)
        {
            plan.Annotations.Add(row);
            context.Db.Add(row);
        }
    }

    /// <summary>The V2 cell of a V1 requirement id: defeat-all (and the score pseudo-objectives) is 0, a lane
    /// objective name is its catalog index + 1, matched as team objectives are.</summary>
    private static int? ObjectiveId(GameCatalogLreTrackView lane, string? requirementId)
    {
        if (requirementId is null)
            return null;
        if (DefeatAllIds.Contains(requirementId))
            return 0;

        var objective = lane.UnitsRestrictions.FirstOrDefault(restriction =>
            V1LegendaryEventObjectiveNames.Matches(requirementId, restriction));
        return objective is null ? null : objective.Index + 1;
    }

    /// <summary>A compact requirement's statuses or states as (battle index, value): an object keyed by index or an
    /// array by position; entries that are not numbers are skipped.</summary>
    private static IEnumerable<(double? BattleIndex, double Value)> ByIndex(JsonElement? values)
    {
        switch (values)
        {
            case { ValueKind: JsonValueKind.Object } byIndex:
                foreach (var entry in byIndex.EnumerateObject().Where(entry => Number(entry.Value) is not null))
                {
                    yield return (
                        double.TryParse(entry.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out var index) ? index : null,
                        Number(entry.Value)!.Value);
                }

                break;
            case { ValueKind: JsonValueKind.Array } byPosition:
                var position = 0;
                foreach (var entry in byPosition.EnumerateArray())
                {
                    if (Number(entry) is { } value)
                        yield return (position, value);
                    position++;
                }

                break;
        }
    }

    private static int Clamp(JsonElement? value, int max, int run, string field, List<V1LegendaryEventIssue> issues)
    {
        var number = Number(value) ?? 0;
        var clamped = (int)Math.Clamp(Math.Floor(number), 0, max);
        if (!number.Equals(clamped))
            issues.Add(new("run_input_clamped", null, $"{run}:{field}:{Format(number)}"));
        return clamped;
    }

    /// <summary>V1 numbers are loose: a JSON number, a finite numeric string or a boolean (as 1/0); anything else
    /// (including "NaN" or "Infinity") is null.</summary>
    private static double? Number(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } number => number.GetDouble(),
        { ValueKind: JsonValueKind.String } text when double.TryParse(
            text.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed) => parsed,
        { ValueKind: JsonValueKind.True } => 1,
        { ValueKind: JsonValueKind.False } => 0,
        _ => null,
    };

    private static string Format(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
