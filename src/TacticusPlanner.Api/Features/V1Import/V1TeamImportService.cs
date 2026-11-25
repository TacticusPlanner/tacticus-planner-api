using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Teams;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>Distinguishes a profile with no V1 teams from one whose <c>teams2</c> is not an array; the import reports
/// those as Skipped and Failed.</summary>
public sealed record V1TeamImportData(bool IsPresent, IReadOnlyList<V1TeamSource>? Teams)
{
    public static V1TeamImportData Missing() => new(false, null);

    public static V1TeamImportData Invalid() => new(true, null);

    public static V1TeamImportData Valid(IReadOnlyList<V1TeamSource> teams) => new(true, teams);
}

/// <summary>
/// One V1 <c>ITeam2</c> entry, read leniently from its raw JSON. <see cref="Name"/> is null when V1 held no string
/// name; <see cref="Chars"/> is null when <c>chars</c> is not an array, which makes the entry unreadable. Unit
/// references keep V1's raw text so an unrecognised one can be reported as it was. A V1 Survival team
/// (<c>survivalTeams</c>) carries its V1 event name in <see cref="SurvivalEventName"/>, its units in <see cref="Chars"/>
/// (characters and MoWs together, as V1 kept them) and the Horde mode.
/// </summary>
public sealed record V1TeamSource(
    string? Name,
    double? Priority,
    IReadOnlyList<string>? Chars,
    int? FlexIndex,
    IReadOnlyList<string> Mows,
    IReadOnlyList<TeamMode> Modes,
    string? CampaignStoryline,
    IReadOnlyList<string> IncursionMows,
    string? Notes,
    string? SurvivalEventName = null)
{
    private static readonly (string Flag, TeamMode Mode)[] ModeFlags =
    [
        ("warOffense", TeamMode.WarOffense),
        ("warDefense", TeamMode.WarDefense),
        ("raid", TeamMode.GuildRaid),
        ("ta", TeamMode.TournamentArena),
        ("horde", TeamMode.Horde),
        ("campaign", TeamMode.Campaign),
        ("incursion", TeamMode.Incursion),
    ];

    public static V1TeamSource Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return new(null, null, null, null, [], [], null, [], null);

        var modes = ModeFlags.Where(flag => Bool(entry, flag.Flag)).Select(flag => flag.Mode).ToList();
        return new(
            String(entry, "name"),
            entry.TryGetProperty("priority", out var priority) && priority.ValueKind == JsonValueKind.Number
                ? priority.GetDouble()
                : null,
            entry.TryGetProperty("chars", out var chars) && chars.ValueKind == JsonValueKind.Array ? References(chars) : null,
            entry.TryGetProperty("flexIndex", out var flex) && flex.ValueKind == JsonValueKind.Number && flex.TryGetInt32(out var index)
                ? index
                : null,
            entry.TryGetProperty("mows", out var mows) && mows.ValueKind == JsonValueKind.Array ? References(mows) : [],
            modes,
            String(entry, "campaignStoryline"),
            entry.TryGetProperty("incursionMows", out var incursions) && incursions.ValueKind == JsonValueKind.Array
                ? References(incursions)
                : [],
            String(entry, "notes"));
    }

    /// <summary>One Survival team per V1 <c>survivalTeams</c> entry with a non-empty list of unit ids, in V1's key
    /// order; anything else is left out.</summary>
    public static IReadOnlyList<V1TeamSource> ReadSurvivalTeams(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } teams)
            return [];

        return
        [
            .. teams.EnumerateObject()
                .Where(team => team.Value.ValueKind == JsonValueKind.Array && team.Value.GetArrayLength() > 0)
                .Select(team => new V1TeamSource(
                    null, null, References(team.Value), null, [], [TeamMode.Horde], null, [], null, team.Name)),
        ];
    }

    private static bool Bool(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? String(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> References(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())];
}

/// <summary>One processed V1 team (<c>v1-team-import</c>).</summary>
/// <param name="Name">The V1 name, or null when V1 held none.</param>
/// <param name="Status"><c>Imported</c>, <c>Skipped</c> or <c>Failed</c>.</param>
/// <param name="Code">Null when imported; otherwise <c>already_imported</c>, <c>duplicate_name</c>,
/// <c>team_limit_reached</c>, <c>no_units</c>, <c>invalid_team</c> or <c>survival_event_not_recognised</c> (a V1 Survival
/// team for an event the catalog does not serve).</param>
/// <param name="TeamId">The created team's id.</param>
/// <param name="Issues"><c>unit_not_recognised</c> (value: the V1 reference), <c>campaign_not_recognised</c> (value:
/// the V1 storyline), <c>duplicate_unit</c> (value: the repeated V1 reference), <c>flex_dropped</c> (V1's flex
/// section began before any kept core character, so every character is core), <c>name_shortened</c>,
/// <c>notes_shortened</c>, and each team rule code the imported team breaks (value null).</param>
public sealed record V1TeamOutcome(
    string? Name,
    ImportPartStatus Status,
    string? Code,
    Guid? TeamId,
    IReadOnlyList<V1TeamIssue> Issues
);

public sealed record V1TeamIssue(string Code, string? Value);

public sealed record V1TeamImportResult(ImportPartResult Part, IReadOnlyList<V1TeamOutcome> Outcomes);

/// <summary>
/// The <c>teams</c> part of the V1 import (<c>v1-team-import</c>): turns V1 <c>teams2</c> into personal teams. Units
/// resolve as the Legendary Event import resolves them (design D7). A resolved team is imported as V1 had it, even
/// when it breaks a team rule, and its broken rules are listed on its outcome. Each imported team stores an immutable
/// import key, so a re-run skips teams an earlier run imported, even renamed ones (design D8). Every team is written in
/// one transaction under the profile lock, so the limit holds against concurrent creates.
/// </summary>
public sealed class V1TeamImportService(PlannerDbContext db, IGameCatalogProvider catalogProvider, TeamsService teams)
{
    /// <summary>The most V1 teams one import processes; any beyond get no outcome.</summary>
    internal const int MaxTeams = 100;

    private static readonly Dictionary<string, string> Storylines = new(StringComparer.Ordinal)
    {
        ["indomitus"] = "campaign1",
        ["indomitusMirror"] = "mirror1",
        ["fallOfCadia"] = "campaign2",
        ["fallOfCadiaMirror"] = "mirror2",
        ["octarius"] = "campaign3",
        ["octariusMirror"] = "mirror3",
        ["saimHann"] = "campaign4",
        ["saimHannMirror"] = "mirror4",
        ["adMech"] = "eventCampaign1",
        ["tyranids"] = "eventCampaign2",
        ["tau"] = "eventCampaign3",
        ["deathGuard"] = "eventCampaign4",
        ["sisters"] = "eventCampaign5",
        ["darkAngels"] = "eventCampaign6",
        ["bloodAngels"] = "eventCampaign7",
    };

    public async Task<V1TeamImportResult> ImportAsync(ProfileId profileId, V1TeamImportData source, CancellationToken ct)
    {
        if (!source.IsPresent)
        {
            return new V1TeamImportResult(
                new ImportPartResult(ImportPartStatus.Skipped, "missing_teams", "The V1 profile has no teams."), []);
        }

        if (source.Teams is null)
        {
            return new V1TeamImportResult(
                new ImportPartResult(ImportPartStatus.Failed, "invalid_teams", "The V1 teams could not be read."), []);
        }

        var catalog = catalogProvider.Current;
        var facts = TeamCatalogFacts.For(catalog);
        var units = new UnitResolver(catalog);
        var candidates = source.Teams
            .Select((team, index) => (Team: team, Index: index))
            .OrderBy(entry => entry.Team.Priority ?? double.MaxValue)
            .ThenBy(entry => entry.Index)
            .Take(MaxTeams)
            .Select(entry => Map(entry.Team, units, facts))
            .ToList();

        // Earlier V1 import parts save but leave their entities tracked (the roster snapshots with their JSON-owned
        // units). Nothing here needs them, and a retried locked attempt would otherwise reload each one.
        db.ChangeTracker.Clear();
        var outcomes = await teams.ExecuteLockedAsync(async transaction =>
        {
            var existing = await db.Teams.AsNoTracking()
                .Select(team => new { team.NameKey, team.ImportKey, team.SortOrder })
                .ToListAsync(ct);
            var importKeys = existing.Where(team => team.ImportKey != null).Select(team => team.ImportKey!).ToHashSet(StringComparer.Ordinal);
            var nameKeys = existing.Select(team => team.NameKey).ToHashSet(StringComparer.Ordinal);
            var count = existing.Count;
            var results = new List<V1TeamOutcome>();

            foreach (var candidate in candidates)
            {
                if (candidate.Outcome is { } decided)
                {
                    results.Add(decided);
                    continue;
                }

                var (name, draft, importKey, issues) = (candidate.Name, candidate.Draft!, candidate.ImportKey!, candidate.Issues);
                if (importKeys.Contains(importKey))
                {
                    results.Add(new(name, ImportPartStatus.Skipped, "already_imported", null, issues));
                    continue;
                }

                if (nameKeys.Contains(TeamValidation.NameKey(draft.Name)))
                {
                    results.Add(new(name, ImportPartStatus.Skipped, "duplicate_name", null, issues));
                    continue;
                }

                if (count >= teams.Limit)
                {
                    results.Add(new(name, ImportPartStatus.Skipped, "team_limit_reached", null, issues));
                    continue;
                }

                count++;
                var team = Team.Create(profileId, draft, count, importKey);
                db.Teams.Add(team);
                importKeys.Add(importKey);
                nameKeys.Add(team.NameKey);
                var ruleIssues = TeamRules.Evaluate(draft, facts)
                    .Select(code => new V1TeamIssue(JsonNamingPolicy.CamelCase.ConvertName(code.ToString()), null));
                results.Add(new(name, ImportPartStatus.Imported, null, team.Id.Value, [.. issues, .. ruleIssues]));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return results;
        }, ct);

        return new V1TeamImportResult(PartResult(outcomes), outcomes);
    }

    private static ImportPartResult PartResult(List<V1TeamOutcome> outcomes)
    {
        if (outcomes.Any(outcome => outcome.Status == ImportPartStatus.Imported))
            return new ImportPartResult(ImportPartStatus.Imported, null, null);
        if (outcomes.Any(outcome => outcome.Status == ImportPartStatus.Failed))
            return new ImportPartResult(ImportPartStatus.Failed, "team_import_failed", "No V1 team could be imported.");
        return new ImportPartResult(ImportPartStatus.Skipped, "teams_skipped", "No new V1 team was imported.");
    }

    /// <summary>A V1 team mapped onto a draft, or the outcome it already has when it cannot be imported.</summary>
    private sealed record Candidate(
        string? Name, TeamDraft? Draft, string? ImportKey, List<V1TeamIssue> Issues, V1TeamOutcome? Outcome);

    private static Candidate Map(V1TeamSource source, UnitResolver units, TeamRuleFacts facts)
    {
        if (source.SurvivalEventName is { } eventName)
            return MapSurvival(source, eventName, units, facts);

        var issues = new List<V1TeamIssue>();
        var trimmed = source.Name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || source.Chars is null)
            return new(source.Name, null, null, issues, new(source.Name, ImportPartStatus.Failed, "invalid_team", null, issues));

        var characters = new List<string>();
        int? flexStart = null;
        for (var index = 0; index < source.Chars.Count; index++)
        {
            if (index == source.FlexIndex)
                flexStart = characters.Count;
            var reference = source.Chars[index];
            if (units.Resolve(reference) is { } id && facts.CharacterFactions.ContainsKey(id))
            {
                if (characters.Contains(id, StringComparer.Ordinal))
                    issues.Add(new("duplicate_unit", reference));
                else
                    characters.Add(id);
            }
            else
            {
                issues.Add(new("unit_not_recognised", reference));
            }
        }

        // A V2 team has at least one core character, so a V1 flex section with no kept character before it
        // (flexIndex 0 means all flex in V1) cannot be kept: every character becomes core, and the player is told.
        if (flexStart == 0 && characters.Count > 0)
            issues.Add(new("flex_dropped", null));

        // Null when V1's flexIndex is out of range or leaves no core or no flex character.
        if (source.FlexIndex is not { } flexIndex || flexIndex < 1 || flexIndex >= source.Chars.Count
            || flexStart is null || flexStart < 1 || flexStart >= characters.Count)
            flexStart = null;

        var mows = ResolveMows(source.Mows, units, facts, issues);
        var modes = source.Modes;
        string? campaign = null;
        if (modes.Contains(TeamMode.Campaign) && source.CampaignStoryline is { } storyline)
        {
            if (Storylines.TryGetValue(storyline, out var groupId))
                campaign = groupId;
            else
                issues.Add(new("campaign_not_recognised", storyline));
        }

        var incursions = modes.Contains(TeamMode.Incursion) ? ResolveMows(source.IncursionMows, units, facts, issues) : [];

        if (characters.Count == 0 && mows.Count == 0)
            return new(source.Name, null, null, issues, new(source.Name, ImportPartStatus.Skipped, "no_units", null, issues));

        var name = trimmed;
        if (name.Length > TeamValidation.MaxNameLength)
        {
            name = V1ImportText.CutToLength(name, TeamValidation.MaxNameLength).TrimEnd();
            issues.Add(new("name_shortened", null));
        }

        var notes = TeamValidation.NormalizeNotes(source.Notes);
        if (notes is { Length: > TeamValidation.MaxNotesLength })
        {
            notes = V1ImportText.CutToLength(notes, TeamValidation.MaxNotesLength);
            issues.Add(new("notes_shortened", null));
        }

        var importKey = V1ImportText.CutToLength(
            trimmed.ToLower(CultureInfo.InvariantCulture), TeamValidation.MaxImportKeyLength);

        // V1 has no MoW roles: the first MoW is core and any later ones flex (design D12), so a V1 war team with
        // several MoWs imports valid instead of flagged. V1 has no rarity cap.
        int? mowFlexStart = mows.Count > 1 ? 1 : null;
        var draft = new TeamDraft(name, modes, characters, flexStart, mows, campaign, incursions, notes, mowFlexStart);
        return new(source.Name, draft, importKey, issues, null);
    }

    /// <summary>
    /// A V1 Survival team as a Horde team tied to its event, named <c>Survival · December 2026</c>. Characters and MoWs
    /// are split by what they resolve to; the featured hero, which V1 never stored, is dropped if present. An event the
    /// catalog does not serve (Crescendo 01) is skipped.
    /// </summary>
    private static Candidate MapSurvival(V1TeamSource source, string eventName, UnitResolver units, TeamRuleFacts facts)
    {
        var issues = new List<V1TeamIssue>();
        var survivalEventKey = GameCatalogSurvivalEventIds.FromEventName(eventName);
        if (survivalEventKey is null || facts.SurvivalEvents?.TryGetValue(survivalEventKey, out var survival) != true || survival is null)
        {
            issues.Add(new("survival_event_not_recognised", eventName));
            return new(null, null, null, issues, new(null, ImportPartStatus.Skipped, "survival_event_not_recognised", null, issues));
        }

        var name = $"Survival · {string.Join(' ', survivalEventKey.Split('-').Select(part =>
            string.Concat(char.ToUpperInvariant(part[0]).ToString(), part[1..])))}";
        var characters = new List<string>();
        var mows = new List<string>();
        foreach (var reference in source.Chars ?? [])
        {
            var id = units.Resolve(reference);
            var target = id is null ? null
                : facts.CharacterFactions.ContainsKey(id) ? characters
                : facts.MowIds.Contains(id) ? mows
                : null;
            if (target is null)
                issues.Add(new("unit_not_recognised", reference));
            else if (id == survival.FeaturedCharacterId)
                continue;
            else if (target.Contains(id!, StringComparer.Ordinal))
                issues.Add(new("duplicate_unit", reference));
            else
                target.Add(id!);
        }

        if (characters.Count == 0 && mows.Count == 0)
            return new(name, null, null, issues, new(name, ImportPartStatus.Skipped, "no_units", null, issues));

        var importKey = V1ImportText.CutToLength(
            $"survival:{eventName.ToLower(CultureInfo.InvariantCulture)}", TeamValidation.MaxImportKeyLength);
        var draft = new TeamDraft(
            name, [TeamMode.Horde], characters, null, mows, null, [], null, SurvivalEventKey: survivalEventKey);
        return new(name, draft, importKey, issues, null);
    }

    private static List<string> ResolveMows(
        IReadOnlyList<string> references, UnitResolver units, TeamRuleFacts facts, List<V1TeamIssue> issues)
    {
        var ids = new List<string>();
        foreach (var reference in references)
        {
            if (units.Resolve(reference) is { } id && facts.MowIds.Contains(id))
            {
                if (ids.Contains(id, StringComparer.Ordinal))
                    issues.Add(new("duplicate_unit", reference));
                else
                    ids.Add(id);
            }
            else
            {
                issues.Add(new("unit_not_recognised", reference));
            }
        }

        return ids;
    }
}
