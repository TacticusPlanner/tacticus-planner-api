using System.Runtime.CompilerServices;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.Teams;

/// <summary>
/// Builds the plain <see cref="TeamRuleFacts"/> the team rules read from a catalog snapshot (design D3), once per
/// snapshot. A campaign is a non-Elite campaign group: Elite and Elite Mirror are difficulties of the same storyline,
/// so their groups are not campaigns a team can name (D5). An Incursion's allowed factions are the
/// <c>allowedFactions</c> of its served <c>incursions</c> record (D6). A Survival event's facts are its served
/// <c>survival-events</c> rules.
/// </summary>
public static class TeamCatalogFacts
{
    private static readonly ConditionalWeakTable<GameCatalogSnapshot, TeamRuleFacts> Cache = new();

    public static TeamRuleFacts For(GameCatalogSnapshot catalog) => Cache.GetValue(catalog, Build);

    private static TeamRuleFacts Build(GameCatalogSnapshot catalog)
    {
        var characterFactions = catalog.CharacterViews
            .ToDictionary(character => character.Id, character => character.Faction, StringComparer.Ordinal);
        var mowIds = catalog.MowList.Select(mow => mow.Id).ToHashSet(StringComparer.Ordinal);
        // Every battle of a group serves the group's allies.
        var campaigns = catalog.CampaignDefinitionViews
            .Where(group => !IsElite(group))
            .ToDictionary(
                group => group.GroupId,
                group => new TeamCampaignFacts(
                    AlliesFactions(catalog.CampaignBattlesByGroup[group.GroupId]).ToHashSet(StringComparer.Ordinal),
                    group.CoreCharacters.ToArray()),
                StringComparer.Ordinal);
        var incursions = catalog.IncursionViews.ToDictionary(
            incursion => incursion.MowId,
            IReadOnlySet<string> (incursion) => incursion.AllowedFactions.ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var survivalEvents = catalog.SurvivalEventViews.ToDictionary(
            survivalEvent => survivalEvent.Id,
            survivalEvent => new TeamSurvivalFacts(
                survivalEvent.FeaturedCharacterId,
                survivalEvent.Rules.AllowedFactionIds.ToHashSet(StringComparer.Ordinal),
                survivalEvent.Rules.MaxCharacters,
                survivalEvent.Rules.MowsAllowed),
            StringComparer.Ordinal);

        return new TeamRuleFacts(characterFactions, mowIds, campaigns, incursions, survivalEvents);
    }

    private static IReadOnlyList<string> AlliesFactions(IReadOnlyList<GameCatalogCampaignBattleView> battles) =>
        battles.Count > 0 ? battles[0].AlliesFactions : [];

    /// <summary>A group is Elite when any of its campaign types is Elite or EliteMirror.</summary>
    private static bool IsElite(GameCatalogCampaignDefinitionView group) =>
        group.Types.Any(type => type is "Elite" or "EliteMirror");
}
