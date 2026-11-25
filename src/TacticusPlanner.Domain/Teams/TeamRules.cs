namespace TacticusPlanner.Domain.Teams;

/// <summary>A campaign a team can be built for: a non-Elite campaign group's allowed factions and core characters.</summary>
public sealed record TeamCampaignFacts(IReadOnlySet<string> AlliesFactions, IReadOnlyList<string> CoreCharacterIds);

/// <summary>
/// A Survival event a team can be built for: its featured hero (always fights, never stored), the factions its battle
/// allows and its caps on stored characters and MoWs (0 MoWs when the event bars them).
/// </summary>
public sealed record TeamSurvivalFacts(
    string FeaturedCharacterId, IReadOnlySet<string> AllowedFactions, int MaxCharacters, int MaxMows);

/// <summary>
/// The catalog facts <see cref="TeamRules"/> reads, as plain values so the domain keeps no catalog reference
/// (design D3). Built once per catalog snapshot by the API.
/// </summary>
public sealed record TeamRuleFacts(
    IReadOnlyDictionary<string, string> CharacterFactions,
    IReadOnlySet<string> MowIds,
    IReadOnlyDictionary<string, TeamCampaignFacts> Campaigns,
    IReadOnlyDictionary<string, IReadOnlySet<string>> IncursionFactionsByMowId,
    IReadOnlyDictionary<string, TeamSurvivalFacts>? SurvivalEvents = null
);

/// <summary>
/// The one rule evaluator for saves, reads and the V1 import (design D3), so they cannot disagree. Returns every rule a
/// draft breaks, in the order of <see cref="TeamRuleCode"/>; an empty list means the draft may be saved. Locked and
/// unowned units are never considered: the rules do not read the player's roster.
/// </summary>
public static class TeamRules
{
    private static readonly TeamMode[] SizeLimitedModes = [TeamMode.WarOffense, TeamMode.WarDefense, TeamMode.TournamentArena];

    public static IReadOnlyList<TeamRuleCode> Evaluate(TeamDraft draft, TeamRuleFacts facts)
    {
        var broken = new SortedSet<TeamRuleCode>();
        var modes = draft.Modes.ToHashSet();
        var campaign = modes.Contains(TeamMode.Campaign);
        var incursion = modes.Contains(TeamMode.Incursion);

        if (draft.Name.Trim().Length is < TeamValidation.MinNameLength or > TeamValidation.MaxNameLength)
            broken.Add(TeamRuleCode.NameLength);
        if (modes.Count == 0)
            broken.Add(TeamRuleCode.NoMode);
        if (draft.CharacterIds.Count < TeamValidation.MinCharacters)
            broken.Add(TeamRuleCode.TooFewCharacters);
        if (draft.CharacterIds.Any(id => !facts.CharacterFactions.ContainsKey(id)) || draft.MowIds.Any(id => !facts.MowIds.Contains(id)))
            broken.Add(TeamRuleCode.UnknownUnit);
        if ((campaign || incursion) && modes.Count > 1)
            broken.Add(TeamRuleCode.ModeNotCombinable);

        if (SizeLimitedModes.Any(modes.Contains))
        {
            if (draft.CoreCount > TeamValidation.MaxWarCoreCharacters)
                broken.Add(TeamRuleCode.TooManyCoreCharacters);
            if (draft.CoreMowCount > TeamValidation.MaxWarCoreMows)
                broken.Add(TeamRuleCode.TooManyCoreMows);
        }

        if ((campaign || incursion) && draft.MowIds.Count > 0)
            broken.Add(TeamRuleCode.MowsNotAllowed);

        EvaluateCampaign(draft, facts, campaign, broken);
        EvaluateIncursion(draft, facts, incursion, broken);
        EvaluateSurvival(draft, facts, modes, broken);

        return [.. broken];
    }

    private static void EvaluateCampaign(TeamDraft draft, TeamRuleFacts facts, bool campaign, SortedSet<TeamRuleCode> broken)
    {
        if (campaign != (draft.CampaignGroupId is not null))
            broken.Add(TeamRuleCode.CampaignRequired);
        if (draft.CampaignGroupId is null)
            return;
        if (!facts.Campaigns.TryGetValue(draft.CampaignGroupId, out var group))
        {
            broken.Add(TeamRuleCode.UnknownCampaign);
            return;
        }

        if (!campaign)
            return;
        var core = draft.CharacterIds.Take(draft.CoreCount).ToHashSet(StringComparer.Ordinal);
        if (group.CoreCharacterIds.Any(id => !core.Contains(id)))
            broken.Add(TeamRuleCode.CoreCharacterMissing);
        if (!AllCharactersFrom(draft, facts, group.AlliesFactions))
            broken.Add(TeamRuleCode.FactionNotAllowed);
    }

    private static void EvaluateIncursion(TeamDraft draft, TeamRuleFacts facts, bool incursion, SortedSet<TeamRuleCode> broken)
    {
        if (incursion != (draft.IncursionMowIds.Count > 0))
            broken.Add(TeamRuleCode.IncursionRequired);
        if (draft.IncursionMowIds.Count == 0)
            return;

        var known = new List<IReadOnlySet<string>>();
        foreach (var mowId in draft.IncursionMowIds)
        {
            if (facts.IncursionFactionsByMowId.TryGetValue(mowId, out var factions))
                known.Add(factions);
            else
                broken.Add(TeamRuleCode.UnknownIncursion);
        }

        if (known.Count == 0)
            return;
        var shared = new HashSet<string>(known[0], StringComparer.Ordinal);
        foreach (var factions in known.Skip(1))
            shared.IntersectWith(factions);
        if (shared.Count == 0)
            broken.Add(TeamRuleCode.NoSharedIncursionFaction);
        if (incursion && !AllCharactersFrom(draft, facts, shared))
            broken.Add(TeamRuleCode.FactionNotAllowed);
    }

    /// <summary>
    /// A team tied to a Survival event fields the Horde mode alone, does not store the event's featured hero (it always
    /// fights), stays within the event's character and MoW caps and takes characters only from allowed factions.
    /// </summary>
    private static void EvaluateSurvival(TeamDraft draft, TeamRuleFacts facts, HashSet<TeamMode> modes, SortedSet<TeamRuleCode> broken)
    {
        if (draft.SurvivalEventKey is null)
            return;
        if (!modes.SetEquals([TeamMode.Horde]))
            broken.Add(TeamRuleCode.SurvivalModeRequired);
        if (facts.SurvivalEvents?.TryGetValue(draft.SurvivalEventKey, out var survival) != true || survival is null)
        {
            broken.Add(TeamRuleCode.UnknownSurvivalEvent);
            return;
        }

        if (draft.CharacterIds.Contains(survival.FeaturedCharacterId, StringComparer.Ordinal))
            broken.Add(TeamRuleCode.SurvivalFeaturedCharacter);
        if (draft.CharacterIds.Count > survival.MaxCharacters)
            broken.Add(TeamRuleCode.SurvivalTooManyCharacters);
        if (draft.MowIds.Count > 0 && survival.MaxMows == 0)
            broken.Add(TeamRuleCode.MowsNotAllowed);
        else if (draft.MowIds.Count > survival.MaxMows)
            broken.Add(TeamRuleCode.SurvivalTooManyMows);
        if (!AllCharactersFrom(draft, facts, survival.AllowedFactions))
            broken.Add(TeamRuleCode.FactionNotAllowed);
    }

    /// <summary>Whether every known character is of an allowed faction; unknown characters are reported as
    /// <see cref="TeamRuleCode.UnknownUnit"/> instead.</summary>
    private static bool AllCharactersFrom(TeamDraft draft, TeamRuleFacts facts, IReadOnlySet<string> allowed) =>
        draft.CharacterIds.All(id => !facts.CharacterFactions.TryGetValue(id, out var faction) || allowed.Contains(faction));
}
