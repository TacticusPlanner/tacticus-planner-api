using System.Runtime.CompilerServices;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.GuildWars;

/// <summary>
/// Builds the plain <see cref="GuildWarRuleFacts"/> the war rules read from a catalog snapshot, once per snapshot: the
/// catalog's character and MoW ids and, per served <c>guild-war</c> season, its zones' tier at each battlefield level,
/// its tiers' slot caps, its buffs and its boards.
/// </summary>
public static class GuildWarCatalogFacts
{
    private static readonly ConditionalWeakTable<GameCatalogSnapshot, GuildWarRuleFacts> Cache = new();

    public static GuildWarRuleFacts For(GameCatalogSnapshot catalog) => Cache.GetValue(catalog, Build);

    private static GuildWarRuleFacts Build(GameCatalogSnapshot catalog)
    {
        var characterIds = catalog.CharacterViews
            .Select(character => character.Id)
            .ToHashSet(StringComparer.Ordinal);
        var mowIds = catalog.MowList.Select(mow => mow.Id).ToHashSet(StringComparer.Ordinal);
        var seasons = catalog.GuildWarViews.ToDictionary(season => season.Id, SeasonFacts, StringComparer.Ordinal);
        return new GuildWarRuleFacts(characterIds, mowIds, seasons);
    }

    private static GuildWarSeasonFacts SeasonFacts(GameCatalogGuildWarSeason season) => new(
        season.MaxAttempts,
        season.Levels.ToDictionary(
            level => level.BattlefieldLevel,
            IReadOnlyDictionary<string, string> (level) =>
                level.Zones.ToDictionary(zone => zone.ZoneId, zone => zone.TierId, StringComparer.Ordinal)),
        season.Tiers.ToDictionary(
            tier => tier.Id,
            IReadOnlyList<GuildWarRarity> (tier) => [.. tier.RarityCaps.Select(Enum.Parse<GuildWarRarity>)],
            StringComparer.Ordinal),
        season.Zones.Select(zone => zone.Id).ToHashSet(StringComparer.Ordinal),
        season.Buffs.Select(buff => buff.Id).ToHashSet(StringComparer.Ordinal),
        season.Maps.Select(map => map.BoardId).ToHashSet(StringComparer.Ordinal));
}
