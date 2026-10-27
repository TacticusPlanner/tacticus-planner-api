using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the campaign-battles and campaign-definitions datasets are built from.</summary>
    public const string DatamineCampaignsPath = "gameconfig/battles/campaigns.json";

    private const string CampaignEventKind = "Event";
    private const string CampaignChallengeSuffix = "B";
    private const int CampaignPlayerTeam = 1;

    private static readonly string[] CampaignRankMetals = ["Stone", "Iron", "Bronze", "Silver", "Gold", "Diamond", "Adamantine"];
    private const int CampaignRanksPerMetal = 3;

    [GeneratedRegex(@"^(\d+)(B?)$", RegexOptions.CultureInvariant)]
    private static partial Regex CampaignBattleIdRegex();

    [GeneratedRegex(@"^([^:]+)(?::(\d+)(?:-(\d+))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex CampaignGuaranteedRewardRegex();

    [GeneratedRegex(@"^(\w+)%(\d+)/(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CampaignChanceOfRegex();

    // Campaign kind (the campaigns.json key) -> the drop-chances difficulty of its battles. Event campaigns take
    // the battle type instead (eventStandard / eventExtremis).
    private static readonly Dictionary<string, string> CampaignDropDifficulties = new(StringComparer.Ordinal)
    {
        ["Standard"] = "standard",
        ["Mirror"] = "mirror",
        ["Elite"] = "elite",
        ["EliteMirror"] = "elite",
    };

    /// <summary>
    /// Reads every curated campaign group from the datamine <c>campaigns.json</c>, keyed by its campaign-battles
    /// dataset key. Group metadata is curated; each battle (id, energy, node, slots, rewards, power and enemies) is
    /// read from the game, in game order, campaign by campaign in the curated type order.
    /// </summary>
    /// <exception cref="InvalidOperationException">A curated campaign, battle, reward or enemy cannot be read.</exception>
    public static Dictionary<string, GameCatalogCampaignGroup> ReadDatamineCampaigns(
        IReadOnlyDictionary<string, GameCatalogCampaignCuration> curation,
        IReadOnlyDictionary<string, List<DatamineCampaign>> campaigns,
        IReadOnlyDictionary<string, DatamineNpc> npcs,
        IReadOnlyDictionary<string, DatamineUpgrade> upgrades)
    {
        var byId = campaigns
            .SelectMany(kind => kind.Value.Select(campaign => (Kind: kind.Key, Campaign: campaign)))
            .ToDictionary(entry => entry.Campaign.Id, StringComparer.Ordinal);

        var groups = new Dictionary<string, GameCatalogCampaignGroup>(StringComparer.Ordinal);
        foreach (var (key, group) in curation)
        {
            var battles = group.Campaigns.SelectMany(source =>
            {
                var (kind, campaign) = byId.TryGetValue(source.Campaign, out var found)
                    ? found
                    : throw CampaignError(key, $"names campaign '{source.Campaign}' the game does not have");
                return campaign.Battles.Select(battle => ReadCampaignBattle(key, kind, source, battle, npcs, upgrades));
            }).ToArray();

            groups[key] = new GameCatalogCampaignGroup(
                group.GroupId,
                group.Faction,
                group.ReleaseType,
                group.AlliesAlliance,
                group.AlliesFactions,
                group.CoreCharacters,
                group.Campaigns.Select(source => source.Type).ToArray(),
                battles);
        }

        return groups;
    }

    private static GameCatalogCampaignBattle ReadCampaignBattle(
        string key,
        string kind,
        GameCatalogCampaignSourceCuration source,
        DatamineCampaignBattle battle,
        IReadOnlyDictionary<string, DatamineNpc> npcs,
        IReadOnlyDictionary<string, DatamineUpgrade> upgrades)
    {
        var number = CampaignBattleIdRegex().Match(battle.BattleId);
        if (!number.Success)
        {
            throw CampaignError(key, $"campaign '{source.Campaign}' has battle id '{battle.BattleId}' that is not a node number");
        }

        var node = int.Parse(number.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var challenge = number.Groups[2].Length > 0;
        // V1's ids: "{prefix}{node:00}" for a battle, "{prefix}C{node}B" for an event challenge battle.
        var id = challenge
            ? $"{source.BattleIdPrefix}C{node.ToString(CultureInfo.InvariantCulture)}{CampaignChallengeSuffix}"
            : source.BattleIdPrefix + number.Groups[1].Value;

        if (battle.Units.Count != battle.PlayerTeams.Count)
        {
            throw CampaignError(key, $"battle '{id}' has {battle.Units.Count} unit teams for {battle.PlayerTeams.Count} player teams");
        }

        // Enemies are every indexed unit of the teams that do not fight with the player, counted per "npcId:index"
        // and ordered by npc id, then index.
        var enemies = battle.Units
            .Zip(battle.PlayerTeams)
            .Where(team => team.Second != CampaignPlayerTeam)
            .SelectMany(team => team.First)
            .Where(unit => unit.Contains(':', StringComparison.Ordinal))
            .Select(unit =>
            {
                var separator = unit.LastIndexOf(':');
                return int.TryParse(unit.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    ? (Unit: unit, NpcId: unit[..separator], Index: index)
                    : throw CampaignError(key, $"battle '{id}' has enemy '{unit}' without a numeric index");
            })
            .GroupBy(enemy => enemy.Unit, StringComparer.Ordinal)
            .Select(enemies => (enemies.First().NpcId, enemies.First().Index, Count: enemies.Count()))
            .OrderBy(enemy => enemy.NpcId, StringComparer.Ordinal)
            .ThenBy(enemy => enemy.Index)
            .ToArray();

        var detailed = enemies.Select(enemy =>
        {
            var npc = npcs.TryGetValue(enemy.NpcId, out var found)
                ? found
                : throw CampaignError(key, $"battle '{id}' names enemy '{enemy.NpcId}' the game does not have");
            var row = CampaignEnemyStatRow(key, id, enemy.NpcId, enemy.Index, npc);
            return new GameCatalogCampaignDetailedEnemy(enemy.NpcId, npc.Name, enemy.Count, row.StarLevel, CampaignRankName(key, id, row.Rank));
        }).ToArray();
        var enemyNpcs = enemies.Select(enemy => npcs[enemy.NpcId]).ToArray();

        return new GameCatalogCampaignBattle(
            id,
            source.Type,
            challenge,
            battle.StaminaCost ?? 0,
            node,
            battle.Spawnpoints,
            new GameCatalogCampaignRewards(
                (battle.Loot.Base ?? []).Select(reward => ReadCampaignGuaranteedReward(key, id, reward)).ToArray(),
                battle.Loot.ChanceOf is { } chanceOf
                    ? [ReadCampaignPotentialReward(key, id, kind, source.Type, challenge, chanceOf, upgrades)]
                    : []),
            battle.Power,
            enemyNpcs.Select(npc => npc.GrandAllianceId ?? "Neutral").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            enemyNpcs.Select(npc => npc.FactionId ?? "Objects").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            enemies.Sum(enemy => enemy.Count),
            enemyNpcs.Select(npc => npc.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            enemies.Select(enemy => JsonSerializer.SerializeToElement(
                new { id = $"{enemy.NpcId}:{enemy.Index.ToString(CultureInfo.InvariantCulture)}", count = enemy.Count })).ToArray(),
            detailed);
    }

    // The stat row an enemy's index points at, as V1 read it: a boss index is 1-based, any other npc's is 0-based
    // and capped at the last row.
    private static DatamineNpcStat CampaignEnemyStatRow(string key, string battleId, string npcId, int index, DatamineNpc npc)
    {
        if (npc.Stats.Count == 0)
        {
            throw CampaignError(key, $"battle '{battleId}' enemy '{npcId}' has no stat rows");
        }

        var row = npcId.Contains("Boss", StringComparison.Ordinal) ? index - 1 : Math.Min(index, npc.Stats.Count - 1);
        return row >= 0 && row < npc.Stats.Count
            ? npc.Stats[row]
            : throw CampaignError(key, $"battle '{battleId}' enemy '{npcId}:{index.ToString(CultureInfo.InvariantCulture)}' points past its {npc.Stats.Count} stat rows");
    }

    private static string CampaignRankName(string key, string battleId, int rank) =>
        rank >= 0 && rank / CampaignRanksPerMetal < CampaignRankMetals.Length
            ? CampaignRankMetals[rank / CampaignRanksPerMetal] + ((rank % CampaignRanksPerMetal) + 1).ToString(CultureInfo.InvariantCulture)
            : throw CampaignError(key, $"battle '{battleId}' has an enemy at unknown rank {rank.ToString(CultureInfo.InvariantCulture)}");

    private static GameCatalogCampaignGuaranteedReward ReadCampaignGuaranteedReward(string key, string battleId, string reward)
    {
        var match = CampaignGuaranteedRewardRegex().Match(reward);
        if (!match.Success)
        {
            throw CampaignError(key, $"battle '{battleId}' has unreadable reward '{reward}'");
        }

        // "xpCommon" is one item; "gold:12-18" a range; "shards_x:15" a fixed amount.
        var min = match.Groups[2].Success ? int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : 1;
        var max = match.Groups[3].Success ? int.Parse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture) : min;
        return new GameCatalogCampaignGuaranteedReward(match.Groups[1].Value, min, max);
    }

    // The potential drop and its drop-chances id: "{rewardKind}_{difficulty}", the kind from the reward (shard,
    // mythicShard or the upgrade's rarity), the difficulty from the campaign kind (or the event battle type), with
    // "Challenge" appended for an event challenge battle.
    private static GameCatalogCampaignPotentialReward ReadCampaignPotentialReward(
        string key,
        string battleId,
        string kind,
        string type,
        bool challenge,
        string chanceOf,
        IReadOnlyDictionary<string, DatamineUpgrade> upgrades)
    {
        var match = CampaignChanceOfRegex().Match(chanceOf);
        if (!match.Success)
        {
            throw CampaignError(key, $"battle '{battleId}' has unreadable drop '{chanceOf}'");
        }

        var rewardId = match.Groups[1].Value;
        var rewardKind = rewardId.StartsWith("mythicShards_", StringComparison.Ordinal) ? "mythicShard"
            : rewardId.StartsWith("shards_", StringComparison.Ordinal) ? "shard"
            : upgrades.TryGetValue(rewardId, out var upgrade) ? "upgrade" + upgrade.Rarity
            : throw CampaignError(key, $"battle '{battleId}' drops '{rewardId}', which is not a shard or an upgrade");
        var difficulty = kind == CampaignEventKind
            ? "event" + type
            : CampaignDropDifficulties.TryGetValue(kind, out var known)
                ? known
                : throw CampaignError(key, $"battle '{battleId}' is in unknown campaign kind '{kind}'");

        return new GameCatalogCampaignPotentialReward(rewardId, $"{rewardKind}_{difficulty}{(challenge ? "Challenge" : string.Empty)}");
    }

    private static InvalidOperationException CampaignError(string key, string problem) =>
        new($"GameCatalog dataset '{GameCatalogDatasets.CampaignBattles}': campaign group '{key}' {problem}.");
}
