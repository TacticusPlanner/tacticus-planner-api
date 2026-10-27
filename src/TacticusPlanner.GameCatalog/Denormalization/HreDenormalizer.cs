using System.Globalization;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine Hero Release Events.</summary>
    public const string DatamineHeroReleaseEventsPath = "gameconfig/liveEvents/heroReleaseEvent.json";

    /// <summary>Raw path of the datamine leaderboards.</summary>
    public const string DatamineLeaderboardsPath = "gameconfig/leaderboards.json";

    private const string HreEventPrefix = "hero_release_event_";
    private const string HreLeaderboardPrefix = "heroReleaseLeaderboard";
    private const string HreChestPrefix = "hre_progress_chest_";

    /// <summary>
    /// Builds one record per datamine Hero Release Event, ordered by id: the unlockable hero, how battles score points,
    /// the points milestones, the mission chain (from <c>questId</c> along <c>successorQuests</c>), the leaderboard
    /// (event N reads <c>heroReleaseLeaderboard</c> for N = 1, else <c>heroReleaseLeaderboard_N</c>) with each phase
    /// chest's currency and shards, the chest shop (every <c>hre_progress_chest_*</c> product, by chest id) and the
    /// Oh So Close offer.
    /// </summary>
    /// <exception cref="InvalidOperationException">A source is missing or a reward is unmappable.</exception>
    public static IReadOnlyList<GameCatalogHre> ReadDatamineHres(DatamineHreSources sources, JsonSerializerOptions options)
    {
        var chestShop = ReadHreChestShop(sources);

        return sources.Events
            .Select(source =>
            {
                var id = source.EventName;
                if (!id.StartsWith(HreEventPrefix, StringComparison.Ordinal)
                    || !int.TryParse(id[HreEventPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    throw HreError(id, $"the event name must be {HreEventPrefix}N.");
                }

                var hero = source.UnlockableHero;
                var score = source.ScorePoints ?? throw HreError(id, "it has no scorePoints.");
                if (hero is not { HeroId: { Length: > 0 } heroId, BaseRarity: { Length: > 0 } baseRarity, MaxRarity: { Length: > 0 } maxRarity }
                    || source.TrackingFeaturedFaction is not { Length: > 0 } faction
                    || source.QuestId is not { Length: > 0 } questId
                    || source.OhSoCloseOffer is not { Unlock: not null, Ascension: not null } ohSoClose)
                {
                    throw HreError(id, "it needs an unlockable hero, a featured faction, a questId and an ohSoCloseOffer.");
                }

                var milestones = (score.Rewards ?? []).Select(reward => new GameCatalogHreMilestone(
                    reward.ScorePoints,
                    HreRewardAmount(id, reward.Reward ?? "", "hreCurrency") ?? throw HreError(id, $"milestone reward '{reward.Reward}' is not hreCurrency:N."))).ToArray();
                if (milestones.Length == 0 || milestones.Zip(milestones.Skip(1)).Any(pair => pair.Second.ScorePoints <= pair.First.ScorePoints))
                {
                    throw HreError(id, "its milestones must exist with strictly increasing scorePoints.");
                }

                return new GameCatalogHre(
                    id,
                    heroId,
                    baseRarity,
                    maxRarity,
                    faction,
                    source.GameModes ?? [],
                    new GameCatalogHreScorePoints(
                        score.CampaignScorePointsDifficultyMultiplier,
                        score.CampaignScorePointsLightningVictoryDifficultyMultiplier,
                        score.CampaignScorePointsMainUnitsDeadMultiplier,
                        score.CampaignScorePointsStaminaMultiplier,
                        score.PvpScorePointsDifficultyMultiplier,
                        score.ScorePointsEnemyPowerTable ?? [],
                        score.GuildBossScorePointsDamageTableByRarity is { } table
                            ? new SortedDictionary<string, IReadOnlyList<int>>(table.ToDictionary(), StringComparer.Ordinal)
                            : new SortedDictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)),
                    milestones,
                    ReadHreMissions(id, questId, sources.QuestGroups, options),
                    ReadHreLeaderboard(id, number, sources),
                    chestShop,
                    ohSoClose);
            })
            .OrderBy(hre => hre.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static GameCatalogHreMission[] ReadHreMissions(
        string id,
        string questId,
        IReadOnlyDictionary<string, JsonElement> questGroups,
        JsonSerializerOptions options)
    {
        var groupKey = questId.Split('_')[0];
        var quests = questGroups.TryGetValue(groupKey, out var group)
            ? group.Deserialize<DatamineHreQuestGroup>(options)?.Quests.ToDictionary(quest => quest.Name, StringComparer.Ordinal)
            : null;
        if (quests is null)
        {
            throw HreError(id, $"quest group '{groupKey}' is missing.");
        }

        var missions = new List<GameCatalogHreMission>();
        for (var name = questId; name is not null;)
        {
            if (!quests.TryGetValue(name, out var quest) || missions.Any(mission => mission.Id == name))
            {
                throw HreError(id, $"quest '{name}' is missing from '{groupKey}' or repeats in the chain.");
            }

            var rewards = quest.Rewards ?? [];
            var next = quest.SuccessorQuests is [var successor] ? successor : null;
            missions.Add(new GameCatalogHreMission(
                missions.Count + 1,
                quest.Name,
                next is null && missions.Count > 0,
                rewards.Select(reward => HreRewardAmount(id, reward, "hreCurrency")).Sum() ?? 0,
                rewards,
                (quest.Tasks ?? []).Select(task => new GameCatalogHreMissionTask(
                    task.Name,
                    task.Target,
                    new SortedDictionary<string, string>(task.TaskParameters?.ToDictionary() ?? [], StringComparer.Ordinal))).ToArray()));
            name = next;
        }

        if (missions.Any(mission => mission.Tasks.Count == 0 || mission.Tasks.Any(task => task.Target <= 0)))
        {
            throw HreError(id, "every mission needs tasks with a positive target.");
        }

        return [.. missions];
    }

    private static GameCatalogHreLeaderboard ReadHreLeaderboard(string id, int number, DatamineHreSources sources)
    {
        var key = number == 1 ? HreLeaderboardPrefix : $"{HreLeaderboardPrefix}_{number.ToString(CultureInfo.InvariantCulture)}";
        if (!sources.Leaderboards.Leaderboards.TryGetValue(key, out var leaderboard)
            || leaderboard.PositionRewards is not { Count: > 0 } daily
            || leaderboard.RewardSchedules is not { Count: > 0 } schedules)
        {
            throw HreError(id, $"leaderboard '{key}' is missing or has no position rewards and schedules.");
        }

        return new GameCatalogHreLeaderboard(
            key,
            daily.Select(reward => new GameCatalogHreDailyReward(
                reward.Position,
                HreRewardAmount(id, reward.RewardId, "hreCurrency") ?? throw HreError(id, $"daily reward '{reward.RewardId}' is not hreCurrency:N."))).ToArray(),
            schedules.Select(schedule => new GameCatalogHrePhase(
                schedule.EndOffset?.Days,
                schedule.RewardTiers.Select(tier =>
                {
                    var rewards = HreChestRewards(id, tier.RewardId, sources.Chests);
                    return new GameCatalogHrePhaseChest(
                        tier.PositionAbsolute,
                        tier.RewardId,
                        rewards.Select(reward => HreRewardAmount(id, reward, "hreCurrency")).Sum() ?? 0,
                        rewards.Select(reward => HreRewardRange(id, reward, "hreShards")?.Min).Sum() ?? 0);
                }).ToArray())).ToArray());
    }

    private static GameCatalogHreChest[] ReadHreChestShop(DatamineHreSources sources) =>
        sources.Products
            .Select(pair => pair.Value)
            .Where(product => product.ValueKind == JsonValueKind.Object
                && product.TryGetProperty("chestId", out var chestId)
                && chestId.GetString()?.StartsWith(HreChestPrefix, StringComparison.Ordinal) == true)
            .Select(product =>
            {
                var chestId = product.GetProperty("chestId").GetString()!;
                if (!product.TryGetProperty("cost", out var cost)
                    || !cost.TryGetProperty("type", out var type) || type.GetString() != "hreCurrency"
                    || !cost.TryGetProperty("amount", out var amount) || !amount.TryGetInt32(out var price) || price <= 0)
                {
                    throw HreError("chest shop", $"chest '{chestId}' must cost a positive hreCurrency amount.");
                }

                var shards = HreChestRewards("chest shop", chestId, sources.Chests)
                    .Select(reward => HreRewardRange("chest shop", reward, "hreShards"))
                    .SingleOrDefault(range => range is not null)
                    ?? throw HreError("chest shop", $"chest '{chestId}' holds no hreShards.");
                return new GameCatalogHreChest(chestId, price, shards.Min, shards.Max);
            })
            .OrderBy(chest => chest.ChestId, StringComparer.Ordinal)
            .ToArray();

    private static string[] HreChestRewards(string id, string chestId, IReadOnlyDictionary<string, JsonElement> chests)
    {
        if (!chests.TryGetValue(chestId, out var chest) || chest.ValueKind != JsonValueKind.Array)
        {
            throw HreError(id, $"chest '{chestId}' is missing from chests.json.");
        }

        return chest.EnumerateArray()
            .Where(entry => entry.TryGetProperty("rewards", out _))
            .SelectMany(entry => entry.GetProperty("rewards").EnumerateArray().Select(reward => reward.GetString() ?? ""))
            .ToArray();
    }

    // The amount of a "type:N" reward of the given type, or null for any other reward.
    private static int? HreRewardAmount(string id, string reward, string type) =>
        HreRewardRange(id, reward, type) is { } range
            ? range.Min == range.Max ? range.Min : throw HreError(id, $"reward '{reward}' must be a fixed amount.")
            : null;

    // The range of a "type:N" or "type:N-M" reward of the given type, or null for any other reward.
    private static (int Min, int Max)? HreRewardRange(string id, string reward, string type)
    {
        var parts = reward.Split(':');
        if (parts.Length != 2 || parts[0] != type)
        {
            return null;
        }

        var bounds = parts[1].Split('-');
        return bounds.Length is 1 or 2
            && int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var min)
            && int.TryParse(bounds[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var max)
            && min > 0 && max >= min
                ? (min, max)
                : throw HreError(id, $"reward '{reward}' is not {type}:N or {type}:N-M.");
    }

    private static InvalidOperationException HreError(string id, string message) =>
        new($"Dataset '{GameCatalogDatasets.Hre}' event '{id}': {message}");
}
