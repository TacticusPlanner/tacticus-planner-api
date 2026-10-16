using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// The <c>legendaryEventPlans</c> part of the V1 import (Stage 2 design D9, Stage 3 design D7): resolves each V1
/// event, its teams, units and objectives onto the catalog, and writes one plan per event through
/// <see cref="LegendaryEventPlanWriter"/>, each in its own transaction. A plan that already has teams never gets
/// teams; its run inputs, marks and notes are still evaluated by their own rules and never overwrite V2 values.
/// Every dropped unit, objective, team, run input or mark is reported as an issue on that event's outcome.
/// </summary>
public sealed partial class V1LegendaryEventImportService(
    PlannerDbContext db,
    IGameCatalogProvider catalogProvider,
    LegendaryEventPlanWriter writer,
    ILogger<V1LegendaryEventImportService> logger)
{
    /// <summary>V1 <c>CharactersService.canonicalName</c>'s hard-coded renames.</summary>
    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.Ordinal)
    {
        ["Sho'Syl"] = "tauMarksman",
        ["Re'Vas"] = "tauCrisis",
        ["PoM"] = "tyranParasite",
        ["Abaddon The Despoiler"] = "blackAbaddon",
        ["Winged Tyrant Prime"] = "tyranWingedPrime",
        ["Tan Gi'Da"] = "admecMarshall",
        ["Nauseous Rotbone"] = "deathRotbone",
        ["Sy-Gex"] = "admecDestroyer",
        ["Patermine"] = "genesPatriarch",
    };

    public async Task<V1LegendaryEventImportResult> ImportAsync(
        ProfileId profileId, V1LegendaryEventImportData source, CancellationToken ct)
    {
        if (!source.IsPresent)
        {
            return new V1LegendaryEventImportResult(
                new ImportPartResult("Skipped", "missing_legendary_event_plans", "The V1 profile has no Legendary Event teams."),
                []);
        }

        if (source.Events is null)
        {
            return new V1LegendaryEventImportResult(
                new ImportPartResult("Failed", "invalid_legendary_event_plans", "The V1 Legendary Event teams could not be read."),
                []);
        }

        var catalog = catalogProvider.Current;
        var units = new UnitResolver(catalog);
        var runs = await SyncedRunsAsync(profileId, ct);
        var outcomes = new List<V1LegendaryEventOutcome>();
        foreach (var v1Event in source.Events)
            outcomes.Add(await ImportEventAsync(profileId, catalog, units, runs, source.ShowPaidOptions, v1Event, ct));

        return new V1LegendaryEventImportResult(PartResult(outcomes), outcomes);
    }

    private static ImportPartResult PartResult(List<V1LegendaryEventOutcome> outcomes)
    {
        if (outcomes.Any(outcome => outcome.Status == "Imported"))
            return new ImportPartResult("Imported", null, null);
        if (outcomes.Any(outcome => outcome.Status == "Failed"))
            return new ImportPartResult("Failed", "legendary_event_import_failed", "No Legendary Event teams could be imported.");
        if (outcomes.All(outcome => outcome.Code == "no_legendary_event_imported"))
            return new ImportPartResult("Skipped", "no_legendary_event_imported", "No V1 Legendary Event had teams to import.");
        return new ImportPartResult("Skipped", "legendary_events_skipped",
            "No Legendary Event plan was imported: each V1 event already has a V2 plan with teams, is not available "
            + "in V2 yet, or had no teams to import. See legendaryEventOutcomes for each event.");
    }

    private async Task<V1LegendaryEventOutcome> ImportEventAsync(
        ProfileId profileId,
        GameCatalogSnapshot catalog,
        UnitResolver units,
        IReadOnlyDictionary<string, int> runs,
        bool? showP2POptions,
        V1LegendaryEventSource v1Event,
        CancellationToken ct)
    {
        var eventId = catalog.ServedLreIdForV1Id(v1Event.V1EventId);
        var lre = eventId is null ? null : catalog.LreViews.Single(view => view.Id == eventId);
        if (lre is null)
        {
            return Outcome(null, v1Event, "Skipped", "event_not_in_catalog",
                "This Legendary Event is not available in V2 yet.", 0, 0, 0, []);
        }

        var issues = new List<V1LegendaryEventIssue>();
        var run = runs.GetValueOrDefault(lre.Id, LegendaryEventValidation.MinRun);
        var teams = v1Event.Teams is null ? new List<ResolvedTeam>() : ResolveTeams(lre, units, SourceTeams(v1Event.Teams, units), run, issues);

        var existing = await db.LegendaryEventPlans.AsNoTracking()
            .Where(plan => plan.EventId == lre.Id)
            .Select(plan => new
            {
                plan.Revision,
                plan.Notes,
                HasTeams = plan.Teams.Any(),
                Runs = plan.Runs.Select(entry => entry.Run).ToList(),
                Cells = plan.Annotations
                    .Select(entry => new { entry.LaneId, entry.BattleIndex, entry.ObjectiveId })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
        var writeTeams = existing?.HasTeams != true && teams.Count > 0;
        var runInputs = V1LegendaryEventProgressImport.ResolveRunInputs(
            lre, v1Event.Progress?.Overview, existing?.Runs ?? [], issues);
        var annotations = V1LegendaryEventProgressImport.ResolveAnnotations(
            lre, v1Event.Progress,
            existing?.Cells.Select(cell => (cell.LaneId, cell.BattleIndex, cell.ObjectiveId)).ToHashSet() ?? [],
            issues);
        var notes = ImportedNotes(v1Event.Notes, existing?.Notes, issues);

        if (!writeTeams && runInputs.Count == 0 && annotations.Count == 0 && notes is null)
        {
            return existing?.HasTeams == true
                ? Outcome(lre.Id, v1Event, "Skipped", "plan_already_exists",
                    "A V2 plan with teams already exists for this event and everything else was already in V2; it was "
                    + "left unchanged.", 0, 0, 0, issues)
                : Outcome(lre.Id, v1Event, "Skipped", "no_legendary_event_imported",
                    "Nothing from V1 for this event could be imported.", 0, 0, 0, issues);
        }

        var importedTeams = writeTeams ? teams : [];
        var showPaidOptions = existing is null ? showP2POptions : null;
        try
        {
            var result = await writer.WriteAsync(profileId, lre.Id, existing?.Revision ?? 0, (context, _) =>
            {
                if (notes is not null)
                    context.Plan.Notes = notes;
                if (showPaidOptions is { } paid)
                    context.Plan.ShowPaidOptions = paid;
                foreach (var team in importedTeams)
                    LegendaryEventTeamMutations.CreateTeam(context, team.LaneId, team.Fields);
                V1LegendaryEventProgressImport.Apply(context, runInputs, annotations);
                return Task.FromResult(LegendaryEventPlanMutation.Applied);
            }, ct);
            if (result is LegendaryEventPlanWriteResult.Saved)
            {
                return writeTeams
                    ? Outcome(lre.Id, v1Event, "Imported", "imported",
                        $"Imported {importedTeams.Count} team(s).", importedTeams.Count, runInputs.Count,
                        annotations.Count, issues)
                    : Outcome(lre.Id, v1Event, "Imported", "inputs_imported",
                        "Imported run inputs, marks or notes; no team was imported.", 0, runInputs.Count,
                        annotations.Count, issues);
            }
        }
        catch (DbUpdateException exception)
        {
            LogEventImportFailed(logger, exception, lre.Id);
        }

        db.ChangeTracker.Clear();
        return Outcome(lre.Id, v1Event, "Failed", "legendary_event_import_failed",
            "This event could not be saved. Try again.", 0, 0, 0, issues);
    }

    /// <summary>The V1 notes to write, or null: an existing V2 plan's notes are never overwritten, and notes over
    /// the limit are cut on a text-element boundary (never splitting a surrogate pair or combining sequence).</summary>
    private static string? ImportedNotes(string? v1Notes, string? existingNotes, List<V1LegendaryEventIssue> issues)
    {
        if (v1Notes is null)
            return null;

        if (!string.IsNullOrWhiteSpace(existingNotes))
        {
            issues.Add(new("existing_notes_kept", null, null));
            return null;
        }

        if (v1Notes.Length <= LegendaryEventValidation.MaxNotesLength)
            return v1Notes;

        var cut = 0;
        var elements = StringInfo.GetTextElementEnumerator(v1Notes);
        while (elements.MoveNext())
        {
            var end = elements.ElementIndex + elements.GetTextElement().Length;
            if (end > LegendaryEventValidation.MaxNotesLength)
                break;
            cut = end;
        }

        issues.Add(new("notes_truncated", null, v1Notes.Length.ToString(CultureInfo.InvariantCulture)));
        return v1Notes[..cut];
    }

    /// <summary>V1 teams in order: <c>teams</c> when non-empty, otherwise synthesised from the legacy lane maps the
    /// way V1's <c>populateTeams</c> does (one team per restriction, equal unit sets within a lane merged).</summary>
    private static List<V1LreTeam> SourceTeams(V1LegendaryEventTeams source, UnitResolver units)
    {
        // V1 blobs are user data: null array entries are skipped rather than failing the whole part.
        var teams = source.Teams?.OfType<V1LreTeam>().ToList() ?? [];
        if (teams.Count > 0)
            return teams;

        var synthesised = new List<V1LreTeam>();
        foreach (var lane in LegendaryEventValidation.LaneIds)
        {
            var map = lane switch { "alpha" => source.Alpha, "beta" => source.Beta, _ => source.Gamma };
            foreach (var (restriction, laneReferences) in map ?? [])
            {
                var references = laneReferences?.OfType<string>().ToList() ?? [];
                if (references.Count == 0)
                    continue;

                var canonical = references.Select(units.Canonical).ToHashSet(StringComparer.Ordinal);
                var match = synthesised.FindIndex(team => team.Section == lane
                    && team.CharSnowprintIds!.Select(units.Canonical).ToHashSet(StringComparer.Ordinal).SetEquals(canonical));
                if (match >= 0)
                {
                    if (!synthesised[match].RestrictionsIds!.Contains(restriction))
                        synthesised[match].RestrictionsIds!.Add(restriction);
                    continue;
                }

                synthesised.Add(new V1LreTeam(
                    Name: $"Team {synthesised.Count + 1} - {lane}",
                    Section: lane,
                    RestrictionsIds: [restriction],
                    CharSnowprintIds: [.. references]));
            }
        }

        return synthesised;
    }

    private static List<ResolvedTeam> ResolveTeams(
        GameCatalogLreView lre, UnitResolver units, List<V1LreTeam> sourceTeams, int run, List<V1LegendaryEventIssue> issues)
    {
        var resolved = new List<ResolvedTeam>();
        for (var index = 0; index < sourceTeams.Count; index++)
        {
            var source = sourceTeams[index];
            var name = TeamName(source.Name, index);
            var lane = lre.Lane(source.Section);
            if (lane is null)
            {
                issues.Add(new("unknown_lane", name, source.Section));
                continue;
            }

            var members = ResolveMembers(lane, units, source, name, issues);
            if (members.Count == 0)
            {
                issues.Add(new("empty_team", name, null));
                continue;
            }

            if (members.Count < LegendaryEventValidation.MinMembers)
            {
                issues.Add(new("team_too_small", name, members.Count.ToString(CultureInfo.InvariantCulture)));
                continue;
            }

            var objectives = new List<int>();
            foreach (var v1Name in source.RestrictionsIds ?? [])
            {
                var objective = v1Name is null
                    ? null
                    : lane.UnitsRestrictions.FirstOrDefault(restriction =>
                        V1LegendaryEventObjectiveNames.Matches(v1Name, restriction));
                if (objective is null)
                    issues.Add(new("unknown_objective", name, v1Name));
                else if (!objectives.Contains(objective.Index))
                    objectives.Add(objective.Index);
            }

            int? depth = source.ExpectedBattleClears is { } clears && clears >= 1
                ? Math.Min((int)clears, lane.BattleIds.Count)
                : null;

            var laneId = source.Section!;
            var duplicate = resolved.FirstOrDefault(team => team.LaneId == laneId
                && team.Members.ToHashSet(StringComparer.Ordinal).SetEquals(members));
            if (duplicate is null)
            {
                resolved.Add(new ResolvedTeam(laneId, name, members, objectives, depth, run));
                continue;
            }

            issues.Add(new("duplicate_team_merged", name, duplicate.Name));
            duplicate.Objectives.AddRange(objectives.Where(objective => !duplicate.Objectives.Contains(objective)));
            if (depth is { } laterDepth)
            {
                if (duplicate.Depth is null)
                    duplicate.Depth = laterDepth;
                else if (duplicate.Depth != laterDepth)
                    issues.Add(new("conflicting_depth_discarded", name, laterDepth.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return resolved;
    }

    private static List<string> ResolveMembers(
        GameCatalogLreTrackView lane, UnitResolver units, V1LreTeam source, string teamName, List<V1LegendaryEventIssue> issues)
    {
        var references = source.CharSnowprintIds is { Count: > 0 } snowprintIds
            ? snowprintIds
            : source.CharactersIds is { Count: > 0 } characterIds
                ? characterIds
                : source.Characters?.Select(character => character?.SnowprintId ?? string.Empty).ToList() ?? [];

        var members = new List<string>();
        foreach (var reference in references)
        {
            if (reference is null || units.Resolve(reference) is not { } unitId)
            {
                issues.Add(new("unknown_unit", teamName, reference));
                continue;
            }

            if (!lane.AvailableUnitIds.Contains(unitId))
            {
                issues.Add(new("unit_not_allowed_on_lane", teamName, unitId));
                continue;
            }

            if (members.Contains(unitId))
            {
                issues.Add(new("duplicate_unit", teamName, unitId));
                continue;
            }

            members.Add(unitId);
        }

        if (members.Count > LegendaryEventValidation.MaxMembers)
        {
            issues.Add(new("team_truncated", teamName, members.Count.ToString(CultureInfo.InvariantCulture)));
            members = members.Take(LegendaryEventValidation.MaxMembers).ToList();
        }

        return members;
    }

    private static string TeamName(string? name, int index)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return $"Team {index + 1}";
        return trimmed.Length > LegendaryEventValidation.MaxTeamNameLength
            ? trimmed[..LegendaryEventValidation.MaxTeamNameLength].TrimEnd()
            : trimmed;
    }

    /// <summary>The synced current run per event, from the stored player data (clamped to 1–3).</summary>
    private async Task<IReadOnlyDictionary<string, int>> SyncedRunsAsync(ProfileId profileId, CancellationToken ct)
    {
        var lreProgress = await db.PlayerDataSnapshots.AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.LreProgress)
            .FirstOrDefaultAsync(ct);
        return (lreProgress ?? [])
            .Where(record => record.CurrentEventRun is not null)
            .GroupBy(record => record.Id.Value)
            .ToDictionary(
                group => group.Key,
                group => Math.Clamp(group.First().CurrentEventRun!.Value, LegendaryEventValidation.MinRun, LegendaryEventValidation.MaxRun));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "V1 Legendary Event import failed for {EventId}.")]
    private static partial void LogEventImportFailed(ILogger logger, Exception exception, string eventId);

    private static V1LegendaryEventOutcome Outcome(
        string? eventId, V1LegendaryEventSource v1Event, string status, string code, string message,
        int teamsImported, int runInputsImported, int annotationsImported, IReadOnlyList<V1LegendaryEventIssue> issues) =>
        new(eventId, v1Event.V1EventId, status, code, message, teamsImported, runInputsImported, annotationsImported,
            issues);

    private sealed class ResolvedTeam(string laneId, string name, List<string> members, List<int> objectives, int? depth, int run)
    {
        public string LaneId { get; } = laneId;

        public string Name { get; } = name;

        public List<string> Members { get; } = members;

        public List<int> Objectives { get; } = objectives;

        public int? Depth { get; set; } = depth;

        public LegendaryEventTeamFields Fields =>
            new(Name, Members, null, Objectives, run, Depth, Depth is null ? null : "manual");
    }

    /// <summary>V1 unit references → catalog unit ids: a catalog id, one of V1's renamed units, or a catalog
    /// character name (V1's legacy <c>charactersIds</c> held names).</summary>
    private sealed class UnitResolver(GameCatalogSnapshot catalog)
    {
        private readonly HashSet<string> ids = catalog.Characters.Select(character => character.Id)
            .Concat(catalog.Mows.Select(mow => mow.Id))
            .ToHashSet(StringComparer.Ordinal);

        private readonly Dictionary<string, string> byName = catalog.Characters
            .GroupBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        public string? Resolve(string reference)
        {
            if (ids.Contains(reference))
                return reference;
            if (UnitAliases.TryGetValue(reference, out var alias) && ids.Contains(alias))
                return alias;
            return byName.GetValueOrDefault(reference.Trim());
        }

        /// <summary>For matching unit sets before lane checks: the resolved id, or the reference itself.</summary>
        public string Canonical(string reference) => Resolve(reference) ?? reference;
    }
}

/// <param name="EventId">The catalog event id, or null when the V1 event is not in the catalog.</param>
/// <param name="V1EventId">V1's numeric <c>LegendaryEventEnum</c> key.</param>
/// <param name="Status"><c>Imported</c>, <c>Skipped</c> or <c>Failed</c>.</param>
/// <param name="Code"><c>imported</c> (at least one team written), <c>inputs_imported</c> (run inputs, marks
/// and/or notes written but no team), <c>plan_already_exists</c>, <c>event_not_in_catalog</c>,
/// <c>no_legendary_event_imported</c> or <c>legendary_event_import_failed</c>.</param>
/// <param name="RunInputsImported">Run input rows written.</param>
/// <param name="AnnotationsImported">Maybe clear / Stop here marks written.</param>
/// <param name="Issues">Team-level issues, plus the plan-level notes, run-input and mark issues (whose
/// <c>teamName</c> is null).</param>
public sealed record V1LegendaryEventOutcome(
    string? EventId,
    int V1EventId,
    string Status,
    string Code,
    string Message,
    int TeamsImported,
    int RunInputsImported,
    int AnnotationsImported,
    IReadOnlyList<V1LegendaryEventIssue> Issues
);

/// <summary>Something dropped, truncated, kept or merged while resolving one V1 team, the event's notes, its run
/// inputs or its Maybe clear / Stop here marks.</summary>
/// <param name="Code"><c>unknown_unit</c>, <c>unit_not_allowed_on_lane</c>, <c>duplicate_unit</c>,
/// <c>unknown_objective</c>, <c>unknown_lane</c>, <c>empty_team</c>, <c>team_too_small</c> (one or two units
/// remained; a team needs three), <c>team_truncated</c>, <c>duplicate_team_merged</c> or
/// <c>conflicting_depth_discarded</c>; for the notes, <c>notes_truncated</c> or <c>existing_notes_kept</c> (the
/// V2 plan already had notes, so the V1 notes were not imported); for run inputs, <c>run_input_clamped</c>,
/// <c>unknown_run</c> or <c>existing_run_inputs_kept</c>; for marks, <c>unknown_lane</c>, <c>unknown_battle</c>,
/// <c>unknown_objective</c> or <c>existing_annotations_kept</c>.</param>
/// <param name="TeamName">The V1 team's name; null for a notes, run-input or mark issue.</param>
/// <param name="Value">The dropped unit or objective (null when V1 held a null entry), the merged-into team's
/// name, the discarded depth, the resolved unit count before truncation, the remaining unit count of a team too
/// small to import, the V1 notes' length before truncation, <c>run:field:original</c> for a clamp, the run key,
/// <c>lane:index</c> or <c>lane:name</c> for a skipped mark, or the number of marks kept.</param>
public sealed record V1LegendaryEventIssue(string Code, string? TeamName, string? Value);

public sealed record V1LegendaryEventImportResult(ImportPartResult Part, IReadOnlyList<V1LegendaryEventOutcome> Outcomes);
