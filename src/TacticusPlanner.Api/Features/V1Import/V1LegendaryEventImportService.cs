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
/// The <c>legendaryEventPlans</c> part of the V1 import (design D9): resolves each V1 event, its teams, units and
/// objectives onto the catalog, and writes one plan per event through <see cref="LegendaryEventPlanWriter"/>, each
/// in its own transaction. A plan that already has teams is never touched. Every dropped unit, objective or team
/// is reported as an issue on that event's outcome.
/// </summary>
public sealed partial class V1LegendaryEventImportService(
    PlannerDbContext db,
    IGameCatalogProvider catalogProvider,
    LegendaryEventPlanWriter writer,
    ILogger<V1LegendaryEventImportService> logger)
{
    private static readonly string[] Lanes = ["alpha", "beta", "gamma"];

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
            outcomes.Add(await ImportEventAsync(profileId, catalog, units, runs, v1Event, ct));

        var part = outcomes.Any(outcome => outcome.Status == "Imported")
            ? new ImportPartResult("Imported", null, null)
            : outcomes.Any(outcome => outcome.Status == "Failed")
                ? new ImportPartResult("Failed", "legendary_event_import_failed", "No Legendary Event teams could be imported.")
                : new ImportPartResult("Skipped", "no_legendary_event_imported", "No V1 Legendary Event had teams to import.");
        return new V1LegendaryEventImportResult(part, outcomes);
    }

    private async Task<V1LegendaryEventOutcome> ImportEventAsync(
        ProfileId profileId,
        GameCatalogSnapshot catalog,
        UnitResolver units,
        IReadOnlyDictionary<string, int> runs,
        V1LegendaryEventSource v1Event,
        CancellationToken ct)
    {
        var eventId = catalog.ServedLreIdForV1Id(v1Event.V1EventId);
        var lre = eventId is null ? null : catalog.LreViews.Single(view => view.Id == eventId);
        if (lre is null)
        {
            return Outcome(null, v1Event, "Skipped", "event_not_in_catalog",
                "This Legendary Event is not available in V2 yet.", 0, []);
        }

        var issues = new List<V1LegendaryEventIssue>();
        var run = runs.GetValueOrDefault(lre.Id, LegendaryEventValidation.MinRun);
        var teams = ResolveTeams(lre, units, SourceTeams(v1Event.Teams, units), run, issues);

        var existing = await db.LegendaryEventPlans.AsNoTracking()
            .Where(plan => plan.EventId == lre.Id)
            .Select(plan => new { plan.Revision, HasTeams = plan.Teams.Any() })
            .FirstOrDefaultAsync(ct);
        if (existing?.HasTeams == true)
        {
            return Outcome(lre.Id, v1Event, "Skipped", "plan_already_exists",
                "A V2 plan with teams already exists for this event; it was left unchanged.", 0, issues);
        }

        if (teams.Count == 0 && v1Event.Notes is null)
        {
            return Outcome(lre.Id, v1Event, "Skipped", "no_legendary_event_imported",
                "No V1 team for this event could be resolved.", 0, issues);
        }

        try
        {
            var result = await writer.WriteAsync(profileId, lre.Id, existing?.Revision ?? 0, (context, _) =>
            {
                if (v1Event.Notes is not null)
                    context.Plan.Notes = v1Event.Notes.Length > LegendaryEventValidation.MaxNotesLength
                        ? v1Event.Notes[..LegendaryEventValidation.MaxNotesLength]
                        : v1Event.Notes;
                foreach (var team in teams)
                    LegendaryEventTeamMutations.CreateTeam(context, team.LaneId, team.Fields);
                return Task.FromResult(LegendaryEventPlanMutation.Applied);
            }, ct);
            if (result is LegendaryEventPlanWriteResult.Saved)
            {
                return Outcome(lre.Id, v1Event, "Imported", "imported",
                    $"Imported {teams.Count} team(s).", teams.Count, issues);
            }
        }
        catch (DbUpdateException exception)
        {
            LogEventImportFailed(logger, exception, lre.Id);
        }

        db.ChangeTracker.Clear();
        return Outcome(lre.Id, v1Event, "Failed", "legendary_event_import_failed",
            "The teams for this event could not be saved. Try again.", 0, issues);
    }

    /// <summary>V1 teams in order: <c>teams</c> when non-empty, otherwise synthesised from the legacy lane maps the
    /// way V1's <c>populateTeams</c> does (one team per restriction, equal unit sets within a lane merged).</summary>
    private static List<V1LreTeam> SourceTeams(V1LegendaryEventTeams source, UnitResolver units)
    {
        if (source.Teams is { Count: > 0 } teams)
            return teams;

        var synthesised = new List<V1LreTeam>();
        foreach (var lane in Lanes)
        {
            var map = lane switch { "alpha" => source.Alpha, "beta" => source.Beta, _ => source.Gamma };
            foreach (var (restriction, references) in map ?? [])
            {
                if (references is not { Count: > 0 })
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

            var objectives = new List<int>();
            foreach (var v1Name in source.RestrictionsIds ?? [])
            {
                var objective = lane.UnitsRestrictions.FirstOrDefault(restriction =>
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
                : source.Characters?.Select(character => character.SnowprintId ?? string.Empty).ToList() ?? [];

        var members = new List<string>();
        foreach (var reference in references)
        {
            if (units.Resolve(reference) is not { } unitId)
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
        var snapshot = await db.PlayerDataSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        return (snapshot?.LreProgress ?? [])
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
        int teamsImported, IReadOnlyList<V1LegendaryEventIssue> issues) =>
        new(eventId, v1Event.V1EventId, status, code, message, teamsImported, issues);

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
/// <param name="Code"><c>imported</c>, <c>plan_already_exists</c>, <c>event_not_in_catalog</c>,
/// <c>no_legendary_event_imported</c> or <c>legendary_event_import_failed</c>.</param>
public sealed record V1LegendaryEventOutcome(
    string? EventId,
    int V1EventId,
    string Status,
    string Code,
    string Message,
    int TeamsImported,
    IReadOnlyList<V1LegendaryEventIssue> Issues
);

/// <summary>Something dropped, truncated or merged while resolving one V1 team.</summary>
/// <param name="Code"><c>unknown_unit</c>, <c>unit_not_allowed_on_lane</c>, <c>duplicate_unit</c>,
/// <c>unknown_objective</c>, <c>unknown_lane</c>, <c>empty_team</c>, <c>team_truncated</c>,
/// <c>duplicate_team_merged</c> or <c>conflicting_depth_discarded</c>.</param>
/// <param name="Value">The dropped unit or objective, the merged-into team's name, the discarded depth, or the
/// resolved unit count before truncation.</param>
public sealed record V1LegendaryEventIssue(string Code, string TeamName, string? Value);

public sealed record V1LegendaryEventImportResult(ImportPartResult Part, IReadOnlyList<V1LegendaryEventOutcome> Outcomes);
