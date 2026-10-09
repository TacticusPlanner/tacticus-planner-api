using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// The <c>legendaryEventPlans</c> part of the V1 import (design D9): resolves each V1 event to a catalog
/// event by its raw numeric id, each team's lane, units and objectives onto catalog ids, carries the clear
/// depth under the profile's synced current run, merges duplicate teams, and writes one plan per event in
/// its own transaction through <see cref="LegendaryEventPlanWriter"/>. A plan that already has a team is
/// never touched. Every drop, truncation or merge is reported as an issue on the event's outcome.
/// </summary>
public sealed class V1LegendaryEventImportService(
    PlannerDbContext db,
    IGameCatalogProvider catalog,
    LegendaryEventPlanWriter writer,
    LegendaryEventPlanProjection projection)
{
    public const string ImportedStatus = "Imported";
    public const string SkippedStatus = "Skipped";
    public const string FailedStatus = "Failed";

    // V1 CharactersService.canonicalName's hard-coded renames.
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
            return V1LegendaryEventImportResult.PartOnly(
                SkippedStatus, "missing_legendary_event_plans", "The V1 profile has no Legendary Event teams.");
        }

        if (source.Events is null)
        {
            return V1LegendaryEventImportResult.PartOnly(
                FailedStatus, "invalid_legendary_event_plans", "The V1 Legendary Event teams could not be read.");
        }

        var currentRuns = await LoadCurrentRunsAsync(profileId, ct);
        var outcomes = new List<V1LegendaryEventOutcome>(source.Events.Count);
        foreach (var v1Event in source.Events)
        {
            outcomes.Add(await ImportEventAsync(profileId, v1Event, currentRuns, ct));
        }

        if (outcomes.Any(outcome => outcome.Status == ImportedStatus))
        {
            return new V1LegendaryEventImportResult(new ImportPartResult(ImportedStatus, null, null), outcomes);
        }

        return outcomes.Any(outcome => outcome.Status == FailedStatus)
            ? new V1LegendaryEventImportResult(
                new ImportPartResult(FailedStatus, "legendary_event_import_failed", "At least one Legendary Event could not be imported."),
                outcomes)
            : new V1LegendaryEventImportResult(
                new ImportPartResult(SkippedStatus, "no_legendary_event_imported", "No V1 Legendary Event could be imported."),
                outcomes);
    }

    /// <summary>The run each event is in according to the profile's synced <c>lre-progress</c> chunk
    /// (clamped to 1–3); events without an entry default to run 1 at the call site.</summary>
    private async Task<Dictionary<string, int>> LoadCurrentRunsAsync(ProfileId profileId, CancellationToken ct)
    {
        var snapshot = await db.PlayerDataSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        var runs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in snapshot?.LreProgress ?? [])
        {
            if (entry.CurrentEventRun is { } run)
            {
                runs[entry.Id.Value] = Math.Clamp(run, LegendaryEventPlanRules.MinRun, LegendaryEventPlanRules.MaxRun);
            }
        }

        return runs;
    }

    private async Task<V1LegendaryEventOutcome> ImportEventAsync(
        ProfileId profileId,
        V1LegendaryEventSource v1Event,
        IReadOnlyDictionary<string, int> currentRuns,
        CancellationToken ct)
    {
        var lre = catalog.Current.FindLreByRawId(v1Event.V1EventId);
        if (lre is null)
        {
            return new V1LegendaryEventOutcome(null, v1Event.V1EventId, SkippedStatus, "event_not_in_catalog",
                "The V1 event is not in the V2 Game Catalog.", 0, []);
        }

        var issues = new List<V1LegendaryEventIssue>();
        var sourceTeams = v1Event.Teams.Count > 0 ? v1Event.Teams : SynthesizeLegacyTeams(v1Event);
        var run = currentRuns.GetValueOrDefault(lre.Id, LegendaryEventPlanRules.MinRun);
        var resolved = new List<ResolvedTeam>();
        foreach (var team in sourceTeams)
        {
            if (ResolveTeam(lre, team, run, issues) is { } resolvedTeam)
            {
                resolved.Add(resolvedTeam);
            }
        }

        var merged = MergeDuplicates(resolved, issues);
        var notes = string.IsNullOrWhiteSpace(v1Event.Notes) ? null : v1Event.Notes.Trim();
        if (merged.Count == 0 && notes is null)
        {
            return new V1LegendaryEventOutcome(lre.Id, v1Event.V1EventId, SkippedStatus, "no_teams_resolved",
                "No V1 team of this event could be resolved onto the catalog.", 0, issues);
        }

        try
        {
            var current = await projection.ReadAsync(lre.Id, ct);
            if (current.Teams.Count > 0)
            {
                return AlreadyExists(lre, v1Event, issues);
            }

            var alreadyExists = false;
            var result = await writer.WriteAsync(profileId, lre.Id, current.Revision, plan =>
            {
                if (plan.Teams.Count > 0)
                {
                    alreadyExists = true;
                    return LegendaryEventMutationOutcome.Unchanged;
                }

                if (notes is not null)
                {
                    plan.Notes = notes;
                }

                foreach (var team in merged)
                {
                    LegendaryEventPlanWriter.AppendTeam(plan, team.LaneId, team.Content, team.Depth, writer.Now);
                }

                return LegendaryEventMutationOutcome.Changed;
            }, ct);

            if (alreadyExists)
            {
                return AlreadyExists(lre, v1Event, issues);
            }

            return result is LegendaryEventPlanWriteResult.Ok
                ? new V1LegendaryEventOutcome(lre.Id, v1Event.V1EventId, ImportedStatus, "imported",
                    $"{merged.Count} team(s) imported.", merged.Count, issues)
                : Failed(lre, v1Event, issues);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            return Failed(lre, v1Event, issues);
        }
    }

    private static V1LegendaryEventOutcome AlreadyExists(
        GameCatalogLreView lre, V1LegendaryEventSource v1Event, List<V1LegendaryEventIssue> issues) =>
        new(lre.Id, v1Event.V1EventId, SkippedStatus, "plan_already_exists",
            "The plan already has teams; nothing was changed.", 0, issues);

    private static V1LegendaryEventOutcome Failed(
        GameCatalogLreView lre, V1LegendaryEventSource v1Event, List<V1LegendaryEventIssue> issues) =>
        new(lre.Id, v1Event.V1EventId, FailedStatus, "legendary_event_import_failed",
            "The plan could not be written. Try again.", 0, issues);

    /// <summary>V1's <c>populateTeams</c>: one team per legacy restriction entry, merging identical unit
    /// sets within a lane so the restriction names accumulate on one team.</summary>
    private List<V1LreTeam> SynthesizeLegacyTeams(V1LegendaryEventSource v1Event)
    {
        var teams = new List<V1LreTeam>();
        var lanes = new (string Lane, IReadOnlyDictionary<string, IReadOnlyList<string>> Map)[]
        {
            (GameCatalogLreLookups.AlphaLane, v1Event.LegacyAlpha),
            (GameCatalogLreLookups.BetaLane, v1Event.LegacyBeta),
            (GameCatalogLreLookups.GammaLane, v1Event.LegacyGamma),
        };
        foreach (var (lane, map) in lanes)
        {
            foreach (var (restriction, unitIds) in map)
            {
                var canonical = unitIds.Select(CanonicalUnit).ToHashSet(StringComparer.Ordinal);
                var existingIndex = teams.FindIndex(team => team.Section == lane
                    && (team.CharSnowprintIds ?? []).Select(CanonicalUnit).ToHashSet(StringComparer.Ordinal).SetEquals(canonical));
                if (existingIndex >= 0)
                {
                    var existing = teams[existingIndex];
                    var restrictions = existing.RestrictionsIds ?? [];
                    if (!restrictions.Contains(restriction, StringComparer.Ordinal))
                    {
                        teams[existingIndex] = existing with { RestrictionsIds = [.. restrictions, restriction] };
                    }
                }
                else if (unitIds.Count > 0)
                {
                    teams.Add(new V1LreTeam(
                        null, $"Team {teams.Count + 1} - {lane}", lane, [restriction], unitIds.ToList(), null, null, null));
                }
            }
        }

        return teams;
    }

    private ResolvedTeam? ResolveTeam(GameCatalogLreView lre, V1LreTeam team, int run, List<V1LegendaryEventIssue> issues)
    {
        var teamName = string.IsNullOrWhiteSpace(team.Name) ? "Unnamed team" : team.Name.Trim();
        if (teamName.Length > LegendaryEventPlanRules.MaxTeamNameLength)
        {
            teamName = teamName[..LegendaryEventPlanRules.MaxTeamNameLength].TrimEnd();
        }

        var lane = lre.Lane(team.Section);
        if (lane is null || team.Section is null)
        {
            issues.Add(new("unknown_lane", teamName, team.Section));
            return null;
        }

        var allowed = lane.AvailableUnitIds.ToHashSet(StringComparer.Ordinal);
        var members = new List<string>();
        foreach (var reference in UnitReferences(team))
        {
            var unitId = ResolveUnit(reference);
            if (unitId is null)
            {
                issues.Add(new("unknown_unit", teamName, reference));
            }
            else if (!allowed.Contains(unitId))
            {
                issues.Add(new("unit_not_allowed_on_lane", teamName, unitId));
            }
            else if (members.Contains(unitId, StringComparer.Ordinal))
            {
                issues.Add(new("duplicate_unit", teamName, unitId));
            }
            else
            {
                members.Add(unitId);
            }
        }

        if (members.Count == 0)
        {
            issues.Add(new("empty_team", teamName, null));
            return null;
        }

        if (members.Count > LegendaryEventPlanRules.MaxTeamSize)
        {
            issues.Add(new("team_truncated", teamName, string.Join(",", members.Skip(LegendaryEventPlanRules.MaxTeamSize))));
            members = members.Take(LegendaryEventPlanRules.MaxTeamSize).ToList();
        }

        var objectives = new List<int>();
        foreach (var restriction in team.RestrictionsIds ?? [])
        {
            var objective = V1LegendaryEventObjectiveNames.Resolve(lane, restriction);
            if (objective is null)
            {
                issues.Add(new("unknown_objective", teamName, restriction));
            }
            else if (!objectives.Contains(objective.Index))
            {
                objectives.Add(objective.Index);
            }
        }

        LegendaryEventRunDepthWrite? depth = null;
        if (team.ExpectedBattleClears is > 0 and var clears)
        {
            depth = new LegendaryEventRunDepthWrite(run, Math.Min(clears, lane.BattleIds.Count), LegendaryEventDepthSource.Manual);
        }

        return new ResolvedTeam(team.Section, new LegendaryEventTeamContent(teamName, members, null, objectives), depth);
    }

    private static IEnumerable<string> UnitReferences(V1LreTeam team)
    {
        if (team.CharSnowprintIds is { Count: > 0 })
        {
            return team.CharSnowprintIds;
        }

        if (team.CharactersIds is { Count: > 0 })
        {
            return team.CharactersIds;
        }

        return (team.Characters ?? [])
            .Select(character => character.SnowprintId ?? character.Name)
            .OfType<string>();
    }

    /// <summary>A V1 unit reference as a catalog character id: the id itself, a V1 rename alias, or a
    /// character name; null when nothing matches.</summary>
    private string? ResolveUnit(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var characters = catalog.Current.Characters;
        if (characters.Any(character => string.Equals(character.Id, reference, StringComparison.Ordinal)))
        {
            return reference;
        }

        if (UnitAliases.TryGetValue(reference, out var alias))
        {
            return alias;
        }

        return characters.FirstOrDefault(character =>
            string.Equals(character.Id, reference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(character.Name, reference, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private string CanonicalUnit(string reference) => ResolveUnit(reference) ?? reference;

    /// <summary>Within one lane, teams with identical member sets collapse into the first: objectives
    /// unioned, first name kept, first positive depth kept and a later differing depth reported.</summary>
    private static List<ResolvedTeam> MergeDuplicates(List<ResolvedTeam> teams, List<V1LegendaryEventIssue> issues)
    {
        var merged = new List<ResolvedTeam>();
        foreach (var team in teams)
        {
            var survivorIndex = merged.FindIndex(candidate => candidate.LaneId == team.LaneId
                && candidate.Content.MemberUnitIds.ToHashSet(StringComparer.Ordinal).SetEquals(team.Content.MemberUnitIds));
            if (survivorIndex < 0)
            {
                merged.Add(team);
                continue;
            }

            var survivor = merged[survivorIndex];
            issues.Add(new("duplicate_team_merged", team.Content.Name, survivor.Content.Name));
            var objectives = survivor.Content.ObjectiveIndexes.Union(team.Content.ObjectiveIndexes).Order().ToList();
            var depth = survivor.Depth;
            if (team.Depth is not null)
            {
                if (depth is null)
                {
                    depth = team.Depth;
                }
                else if (depth.ExpectedBattleClears != team.Depth.ExpectedBattleClears)
                {
                    issues.Add(new("conflicting_depth_discarded", team.Content.Name,
                        team.Depth.ExpectedBattleClears?.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            merged[survivorIndex] = survivor with { Content = survivor.Content with { ObjectiveIndexes = objectives }, Depth = depth };
        }

        return merged;
    }

    private sealed record ResolvedTeam(string LaneId, LegendaryEventTeamContent Content, LegendaryEventRunDepthWrite? Depth);
}

/// <param name="EventId">The catalog event id, null when the V1 event is not in the catalog.</param>
/// <param name="Status"><c>Imported</c>, <c>Skipped</c> or <c>Failed</c>.</param>
public sealed record V1LegendaryEventOutcome(
    string? EventId,
    int V1EventId,
    string Status,
    string Code,
    string Message,
    int TeamsImported,
    IReadOnlyList<V1LegendaryEventIssue> Issues
);

/// <summary>One dropped unit or objective, skipped team, truncation or merge, with the team it concerns and
/// the value that was dropped (a unit reference, an objective name, a discarded depth) when there is one.</summary>
public sealed record V1LegendaryEventIssue(string Code, string TeamName, string? Value);

public sealed record V1LegendaryEventImportResult(ImportPartResult Part, IReadOnlyList<V1LegendaryEventOutcome> Outcomes)
{
    public static V1LegendaryEventImportResult PartOnly(string status, string code, string message) =>
        new(new ImportPartResult(status, code, message), []);
}
