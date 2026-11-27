using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine Guild War config.</summary>
    public const string DatamineGuildWarPath = "globalconfig/guildWar.json";

    private static readonly string[] GuildWarRarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    private const int GuildWarSlots = 5;

    [GeneratedRegex("^guildWarSeason[A-Z]$")]
    private static partial Regex LiveGuildWarSeason();

    /// <summary>
    /// Builds one guild-war record per live season (key <c>guildWarSeason[A-Z]</c>; test configs dropped), ordered by
    /// key, per the guild-war-dataset spec: zones in layout order, tiers lowest first, levels ascending, buffs by id with
    /// their abilities resolved by id or by rarity suffix, the map pool, scoring and the shared power caps.
    /// </summary>
    /// <exception cref="InvalidOperationException">A season does not hold together (see the spec's load failures).</exception>
    public static IReadOnlyList<GameCatalogGuildWarSeason> ReadDatamineGuildWar(DatamineGuildWar guildWar)
    {
        var seasons = guildWar.GuildWarSeasonConfigs ?? throw InvalidGuildWar("'guildWarSeasonConfigs' is missing.");
        var powerCaps = ReadGuildWarPowerCaps(guildWar.GuildWarConfig?.MaxHeroPowerCap);
        var live = seasons
            .Where(pair => LiveGuildWarSeason().IsMatch(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        if (live.Length == 0)
        {
            throw InvalidGuildWar("no live season (guildWarSeasonA..Z).");
        }

        var ids = live.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        return [.. live.Select(pair => ReadGuildWarSeason(pair.Key, pair.Value, ids, powerCaps))];
    }

    private static GameCatalogGuildWarSeason ReadGuildWarSeason(
        string id,
        DatamineGuildWarSeason season,
        HashSet<string> seasonIds,
        IReadOnlyList<GameCatalogGuildWarPowerCap> powerCaps)
    {
        InvalidOperationException Invalid(string message) => InvalidGuildWar($"season '{id}': {message}");

        var maxAttempts = season.MaxAttempts is { } attempts and > 0
            ? attempts
            : throw Invalid("'maxAttempts' must be positive.");
        var next = season.NextGuildWarSeasonConfigId is { } nextId && seasonIds.Contains(nextId)
            ? nextId
            : throw Invalid($"'nextGuildWarSeasonConfigId' '{season.NextGuildWarSeasonConfigId}' is not a live season.");

        var buffs = ReadGuildWarBuffs(season, Invalid);
        var zones = ReadGuildWarZones(season, buffs, Invalid);
        var tiers = ReadGuildWarTiers(season, Invalid);
        var levels = ReadGuildWarLevels(season, zones, tiers, Invalid);
        var maps = (season.MapPool ?? throw Invalid("'mapPool' is missing."))
            .Select(map => new GameCatalogGuildWarMap(
                string.IsNullOrEmpty(map.BoardId) ? throw Invalid("a map has no boardId.") : map.BoardId,
                map.SpawnPointsSet ?? 0))
            .ToArray();
        if (maps.Length == 0)
        {
            throw Invalid("'mapPool' is empty.");
        }

        var scoring = new GameCatalogGuildWarScoring(
            season.ScoreFromUnitKilled ?? throw Invalid("'scoreFromUnitKilled' is missing."),
            [.. (season.ScoreForLineupEliminated ?? throw Invalid("'scoreForLineupEliminated' is missing."))
                .Select(row => new GameCatalogGuildWarLineupScore(
                    row.MaxAttempts ?? throw Invalid("a lineup score has no maxAttempts."),
                    row.MaxUnitsLost ?? throw Invalid("a lineup score has no maxUnitsLost."),
                    row.Score ?? throw Invalid("a lineup score has no score.")))]);

        return new GameCatalogGuildWarSeason(
            id, next, maxAttempts, zones, tiers, levels, [.. buffs.Values], maps, scoring, powerCaps);
    }

    private static SortedDictionary<string, GameCatalogGuildWarBuff> ReadGuildWarBuffs(
        DatamineGuildWarSeason season, Func<string, InvalidOperationException> invalid)
    {
        var abilities = season.Abilities ?? throw invalid("'abilities' is missing.");
        var buffs = new SortedDictionary<string, GameCatalogGuildWarBuff>(StringComparer.Ordinal);
        foreach (var (buffId, buff) in season.Buffs ?? throw invalid("'buffs' is missing."))
        {
            var abilityId = buff.AbilityId ?? throw invalid($"buff '{buffId}' has no abilityId.");
            var resolved = new List<GameCatalogGuildWarBuffAbility>();
            if (abilities.TryGetValue(abilityId, out var plain))
            {
                resolved.Add(ReadGuildWarBuffAbility(abilityId, null, plain, invalid));
            }

            foreach (var rarity in GuildWarRarities)
            {
                var key = $"{abilityId}_{rarity}";
                if (abilities.TryGetValue(key, out var byRarity))
                {
                    resolved.Add(ReadGuildWarBuffAbility(key, rarity, byRarity, invalid));
                }
            }

            if (resolved.Count == 0)
            {
                throw invalid($"buff '{buffId}' has no ability '{abilityId}' or '{abilityId}_<Rarity>'.");
            }

            buffs[buffId] = new GameCatalogGuildWarBuff(
                buffId, buff.Scope ?? throw invalid($"buff '{buffId}' has no scope."), resolved);
        }

        return buffs;
    }

    private static GameCatalogGuildWarBuffAbility ReadGuildWarBuffAbility(
        string abilityId, string? rarity, DatamineGuildWarAbility ability, Func<string, InvalidOperationException> invalid) =>
        new(
            abilityId,
            rarity,
            new SortedDictionary<string, IReadOnlyList<int>>(
                (ability.Variables ?? []).ToDictionary(
                    pair => pair.Key,
                    IReadOnlyList<int> (pair) => pair.Value?.ToArray()
                        ?? throw invalid($"ability '{abilityId}' variable '{pair.Key}' is null.")),
                StringComparer.Ordinal));

    private static GameCatalogGuildWarZone[] ReadGuildWarZones(
        DatamineGuildWarSeason season,
        SortedDictionary<string, GameCatalogGuildWarBuff> buffs,
        Func<string, InvalidOperationException> invalid)
    {
        var layout = season.DefaultZoneTypeLayout ?? throw invalid("'defaultZoneTypeLayout' is missing.");
        var configs = season.ZoneTypeConfigs ?? throw invalid("'zoneTypeConfigs' is missing.");
        var scores = season.ScorePerZoneType ?? throw invalid("'scorePerZoneType' is missing.");
        var zones = new List<GameCatalogGuildWarZone>();
        for (var row = 0; row < layout.Count; row++)
        {
            for (var column = 0; column < layout[row].Count; column++)
            {
                var id = layout[row][column];
                var config = configs.GetValueOrDefault(id) ?? throw invalid($"zone '{id}' has no zoneTypeConfigs entry.");
                if (config.BuffId is { } buffId && !buffs.ContainsKey(buffId))
                {
                    throw invalid($"zone '{id}' names buff '{buffId}', which is not in 'buffs'.");
                }

                zones.Add(new GameCatalogGuildWarZone(
                    id,
                    config.VisualId ?? throw invalid($"zone '{id}' has no visualId."),
                    config.BuffId,
                    config.CanBeMoved ?? false,
                    scores.TryGetValue(id, out var score) ? score : throw invalid($"zone '{id}' has no score."),
                    row,
                    column));
            }
        }

        if (zones.Count == 0 || zones.Select(zone => zone.Id).Distinct(StringComparer.Ordinal).Count() != zones.Count)
        {
            throw invalid("the layout must list each zone once.");
        }

        return [.. zones];
    }

    private static GameCatalogGuildWarTier[] ReadGuildWarTiers(
        DatamineGuildWarSeason season, Func<string, InvalidOperationException> invalid)
    {
        var tiers = (season.ZoneTiers ?? throw invalid("'zoneTiers' is missing."))
            .Select(tier =>
            {
                var id = tier.ZoneTierId ?? throw invalid("a zone tier has no zoneTierId.");
                var caps = tier.RarityCaps ?? [];
                return caps.Count == GuildWarSlots && caps.All(GuildWarRarities.Contains)
                    ? new GameCatalogGuildWarTier(id, [.. caps])
                    : throw invalid($"tier '{id}' must have {GuildWarSlots} game rarity caps.");
            })
            .OrderBy(tier => Array.IndexOf(GuildWarRarities, tier.RarityCaps[0]))
            .ToArray();
        return tiers.Length > 0 && tiers.Select(tier => tier.Id).Distinct(StringComparer.Ordinal).Count() == tiers.Length
            ? tiers
            : throw invalid("'zoneTiers' must list each tier once.");
    }

    private static GameCatalogGuildWarLevel[] ReadGuildWarLevels(
        DatamineGuildWarSeason season,
        GameCatalogGuildWarZone[] zones,
        GameCatalogGuildWarTier[] tiers,
        Func<string, InvalidOperationException> invalid)
    {
        var tierIds = tiers.Select(tier => tier.Id).ToHashSet(StringComparer.Ordinal);
        var levels = (season.Levels ?? throw invalid("'levels' is missing."))
            .Select(level =>
            {
                var number = level.BattlefieldLevel ?? throw invalid("a level has no battlefieldLevel.");
                var byZone = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var zone in level.Zones ?? [])
                {
                    var zoneId = zone.WarZoneType ?? throw invalid($"level {number} has a zone with no warZoneType.");
                    var tierId = zone.ZoneTierId is { } tier && tierIds.Contains(tier)
                        ? tier
                        : throw invalid($"level {number} zone '{zoneId}' names unknown tier '{zone.ZoneTierId}'.");
                    if (!byZone.TryAdd(zoneId, tierId))
                    {
                        throw invalid($"level {number} lists zone '{zoneId}' twice.");
                    }
                }

                if (byZone.Count != zones.Length || zones.Any(zone => !byZone.ContainsKey(zone.Id)))
                {
                    throw invalid($"level {number} must list every layout zone exactly once.");
                }

                return new GameCatalogGuildWarLevel(
                    number,
                    level.AttemptDebuffPercent ?? 0,
                    level.ScoreFactorPercent,
                    [.. zones.Select(zone => new GameCatalogGuildWarLevelZone(zone.Id, byZone[zone.Id]))]);
            })
            .OrderBy(level => level.BattlefieldLevel)
            .ToArray();
        return levels.Length > 0
            && levels.Select(level => level.BattlefieldLevel).Distinct().Count() == levels.Length
                ? levels
                : throw invalid("'levels' must list each battlefield level once.");
    }

    private static GameCatalogGuildWarPowerCap[] ReadGuildWarPowerCaps(Dictionary<string, int>? caps) =>
        caps is { Count: > 0 } && caps.Keys.All(GuildWarRarities.Contains)
            ? [.. caps
                .OrderBy(pair => Array.IndexOf(GuildWarRarities, pair.Key))
                .Select(pair => new GameCatalogGuildWarPowerCap(pair.Key, pair.Value))]
            : throw InvalidGuildWar("'guildWarConfig.maxHeroPowerCap' must map game rarities to a power.");

    private static InvalidOperationException InvalidGuildWar(string message) =>
        new($"Dataset '{GameCatalogDatasets.GuildWar}': {message}");
}
