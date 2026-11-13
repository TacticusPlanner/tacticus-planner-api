using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>The <c>quests/groups.json</c> groups the seasonal event missions live in.</summary>
    public const string SeasonalDailyQuestGroup = "seasonal_daily";
    public const string SeasonalFreeQuestGroup = "seasonal_seasonal";
    public const string SeasonalPremiumQuestGroup = "seasonal_seasonal_premium";

    [GeneratedRegex("^(?<key>[A-Z][a-z]+[0-9]{4})(?:Week(?<week>[0-9]+))?EventShop$", RegexOptions.CultureInvariant)]
    private static partial Regex EventMerchantKeyPattern();

    [GeneratedRegex("^(?<month>[A-Z][a-z]+)(?<year>[0-9]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex EventKeyPartsPattern();

    /// <summary>
    /// Discovers the seasonal event shops: every <c>liveEventRequired</c> merchant named
    /// <c>&lt;Month&gt;&lt;Year&gt;[Week&lt;N&gt;]EventShop</c>, dated by the calendar's <c>ShopEvent</c> occurrences with
    /// the same <c>eventKey</c>. A key with no merchant or no calendar week is not returned (the first is reported by
    /// the CI calendar check, the second is simply not served). A calendar week whose merchant is missing is returned
    /// with a null merchant for validation to report. See specs/game-event-shops-dataset.
    /// </summary>
    public static IReadOnlyList<GameCatalogRawEventShop> ReadDatamineEventShops(
        IReadOnlyDictionary<string, JsonElement> merchants,
        IReadOnlyList<GameCatalogEventDefinition> eventDefinitions,
        IReadOnlyList<GameCatalogEventOccurrence> eventOccurrences,
        JsonSerializerOptions options)
    {
        var merchantsByEventKey = new Dictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)>(StringComparer.Ordinal);
        foreach (var (merchantKey, merchant) in merchants)
        {
            var match = EventMerchantKeyPattern().Match(merchantKey);
            if (!match.Success || !IsLiveEventMerchant(merchant))
            {
                continue;
            }

            var eventKey = match.Groups["key"].Value;
            if (!merchantsByEventKey.TryGetValue(eventKey, out var entry))
            {
                entry = (null, new SortedDictionary<int, string>());
            }

            if (match.Groups["week"].Success)
            {
                entry.PerWeek[int.Parse(match.Groups["week"].Value, CultureInfo.InvariantCulture)] = merchantKey;
            }
            else
            {
                entry.Shared = merchantKey;
            }

            merchantsByEventKey[eventKey] = entry;
        }

        var weeksByEventKey = ShopEventWeeksByEventKey(eventDefinitions, eventOccurrences);

        var events = new List<GameCatalogRawEventShop>();
        foreach (var (eventKey, weeks) in weeksByEventKey)
        {
            if (!merchantsByEventKey.TryGetValue(eventKey, out var eventMerchants))
            {
                continue;
            }

            var perWeekMerchants = eventMerchants.PerWeek.Count > 0;
            var rawWeeks = weeks
                .Select(week =>
                {
                    var merchantKey = perWeekMerchants
                        ? eventMerchants.PerWeek.GetValueOrDefault(week.Week)
                        : eventMerchants.Shared;
                    var merchant = merchantKey is null
                        ? null
                        : merchants[merchantKey].Deserialize<GameCatalogRawShop>(options);
                    return new GameCatalogRawEventShopWeek(week.Week, merchantKey, merchant, week.Occurrence);
                })
                .ToArray();

            events.Add(new GameCatalogRawEventShop(
                eventKey, weeks[0].Occurrence.DefinitionId, perWeekMerchants, rawWeeks));
        }

        return events
            .OrderBy(shop => shop.Weeks[0].Occurrence.StartUtc)
            .ThenBy(shop => shop.EventKey, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The calendar's <c>ShopEvent</c> occurrences grouped by <c>eventKey</c>, each group ordered by week number. An
    /// occurrence without a well-formed key or week number is skipped (occurrence validation reports it).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<(int Week, GameCatalogEventOccurrence Occurrence)>>
        ShopEventWeeksByEventKey(
            IReadOnlyList<GameCatalogEventDefinition> eventDefinitions,
            IReadOnlyList<GameCatalogEventOccurrence> eventOccurrences)
    {
        var shopEventDefinitionIds = eventDefinitions
            .Where(definition => definition.Type == ShopEventParameters.DefinitionType)
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);

        return eventOccurrences
            .Where(occurrence => shopEventDefinitionIds.Contains(occurrence.DefinitionId))
            .Select(occurrence =>
            {
                var parameters = occurrence.Parameters;
                var eventKey = parameters is not null && parameters.TryGetValue(ShopEventParameters.EventKey, out var key)
                    ? ShopEventParameters.ReadEventKey(key)
                    : null;
                var week = parameters is not null && parameters.TryGetValue(ShopEventParameters.WeekNumber, out var number)
                    ? ShopEventParameters.ReadWeekNumber(number)
                    : null;
                return (EventKey: eventKey, Week: week, Occurrence: occurrence);
            })
            .Where(entry => entry.EventKey is not null && entry.Week is not null)
            .GroupBy(entry => entry.EventKey!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<(int, GameCatalogEventOccurrence)>)group
                    .OrderBy(entry => entry.Week)
                    .ThenBy(entry => entry.Occurrence.StartUtc)
                    .Select(entry => (entry.Week!.Value, entry.Occurrence))
                    .ToArray(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the served <c>event-shops</c> array from the discovered events: slots through the daily-shop variant
    /// projection, ladders from <c>tieredProgressRewards.json</c> + <c>chests.json</c>, missions by name prefix from
    /// the seasonal quest groups. A week with no merchant serves no slots (validation fails the load on it).
    /// </summary>
    /// <exception cref="InvalidOperationException">A ladder or mission reward cannot be parsed, a mission chain is
    /// broken, or a week's mission prefix matches no seasonal quest.</exception>
    public static IReadOnlyList<GameCatalogEventShopView> BuildEventShops(
        IReadOnlyList<GameCatalogRawEventShop> rawEventShops,
        IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> tieredProgressRewards,
        IReadOnlyDictionary<string, JsonElement> chests,
        IReadOnlyDictionary<string, JsonElement> questGroups,
        JsonSerializerOptions options)
    {
        var seasonalQuests = new[] { SeasonalDailyQuestGroup, SeasonalFreeQuestGroup, SeasonalPremiumQuestGroup }
            .ToDictionary(
                group => group,
                group => questGroups.TryGetValue(group, out var element)
                    ? element.Deserialize<DatamineSeasonalQuestGroup>(options)?.Quests ?? []
                    : (IReadOnlyList<DatamineSeasonalQuest>)[],
                StringComparer.Ordinal);

        return rawEventShops.Select(raw => BuildEventShop(raw, tieredProgressRewards, chests, seasonalQuests)).ToArray();
    }

    /// <summary>The served event id for a datamine event key: <c>October2026</c> → <c>october-2026</c>.</summary>
    public static string EventShopId(string eventKey) => DatamineEventKeyPrefix(eventKey, "-");

    /// <summary>The currency every variant of an event's merchants costs: <c>seasonalEventCurrency&lt;EventKey&gt;</c>.</summary>
    public static string EventShopCurrency(string eventKey) => $"seasonalEventCurrency{eventKey}";

    private static GameCatalogEventShopView BuildEventShop(
        GameCatalogRawEventShop raw,
        IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> tieredProgressRewards,
        IReadOnlyDictionary<string, JsonElement> chests,
        IReadOnlyDictionary<string, IReadOnlyList<DatamineSeasonalQuest>> seasonalQuests)
    {
        var datamineKey = DatamineEventKeyPrefix(raw.EventKey, "_");
        var allQuestNames = seasonalQuests.Values.SelectMany(quests => quests).Select(quest => quest.Name).ToArray();

        var weeks = raw.Weeks.Select(week =>
        {
            var progressChestsKey = new[]
                {
                    $"se_progress_chests_{datamineKey}_week{week.Week}",
                    $"se_progress_chests_{datamineKey}",
                }
                .FirstOrDefault(tieredProgressRewards.ContainsKey);

            var weekPrefix = $"{datamineKey}_week{week.Week}";
            var missionPrefix = allQuestNames.Any(name => name.StartsWith($"{weekPrefix}_", StringComparison.Ordinal))
                ? weekPrefix
                : datamineKey;

            return new GameCatalogEventShopWeekView(
                week.Week,
                ReadIncome(week.Occurrence),
                week.Merchant is null ? [] : BuildShopSlots(week.Merchant),
                progressChestsKey is null ? [] : BuildRewardTiers(raw.EventKey, progressChestsKey, tieredProgressRewards, chests),
                BuildMissions(raw.EventKey, week.Week, missionPrefix, seasonalQuests));
        }).ToArray();

        var milestonesKey = $"se_milestones_{datamineKey}";
        var milestones = tieredProgressRewards.ContainsKey(milestonesKey)
            ? BuildRewardTiers(raw.EventKey, milestonesKey, tieredProgressRewards, chests)
            : [];

        return new GameCatalogEventShopView(
            EventShopId(raw.EventKey),
            raw.EventKey,
            raw.DefinitionId,
            EventShopCurrency(raw.EventKey),
            raw.Weeks[0].Occurrence.StartUtc,
            7 * raw.Weeks.Count,
            milestones,
            weeks);
    }

    private static string DatamineEventKeyPrefix(string eventKey, string separator)
    {
        var match = EventKeyPartsPattern().Match(eventKey);
        return match.Success
            ? $"{match.Groups["month"].Value.ToLowerInvariant()}{separator}{match.Groups["year"].Value}"
            : eventKey.ToLowerInvariant();
    }

    private static bool IsLiveEventMerchant(JsonElement merchant) =>
        merchant.ValueKind == JsonValueKind.Object
        && merchant.TryGetProperty("liveEventRequired", out var live)
        && live.ValueKind == JsonValueKind.True;

    private static GameCatalogEventShopIncomeView? ReadIncome(GameCatalogEventOccurrence occurrence)
    {
        var parameters = occurrence.Parameters;
        if (parameters is null)
        {
            return null;
        }

        var free = parameters.TryGetValue(ShopEventParameters.IncomeFree, out var freeValue)
            ? ShopEventParameters.ReadIncome(freeValue)
            : null;
        var bonusShipment = parameters.TryGetValue(ShopEventParameters.IncomeBonusShipment, out var bonusValue)
            ? ShopEventParameters.ReadIncome(bonusValue)
            : null;

        return free is null && bonusShipment is null
            ? null
            : new GameCatalogEventShopIncomeView(free ?? 0, bonusShipment ?? 0);
    }

    private static GameCatalogRewardTierView[] BuildRewardTiers(
        string eventKey,
        string ladderKey,
        IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> tieredProgressRewards,
        IReadOnlyDictionary<string, JsonElement> chests) =>
        tieredProgressRewards[ladderKey]
            .OrderBy(tier => tier.RequiredProgress)
            .Select(tier => new GameCatalogRewardTierView(
                tier.RequiredProgress,
                chests.TryGetValue(tier.ChestRewardId, out var chest)
                    ? ChestRewardStrings(chest).Select(reward => ParseRewardRange(eventKey, ladderKey, reward)).ToArray()
                    : [ParseRewardRange(eventKey, ladderKey, tier.ChestRewardId)]))
            .ToArray();

    /// <summary>Every reward string of every chest under one <c>chests.json</c> key, in source order.</summary>
    private static IEnumerable<string> ChestRewardStrings(JsonElement chestList) =>
        chestList.ValueKind != JsonValueKind.Array
            ? []
            : chestList.EnumerateArray()
                .Where(chest => chest.ValueKind == JsonValueKind.Object
                    && chest.TryGetProperty("rewards", out var rewards)
                    && rewards.ValueKind == JsonValueKind.Array)
                .SelectMany(chest => chest.GetProperty("rewards").EnumerateArray())
                .Select(reward => reward.GetString() ?? string.Empty);

    private static GameCatalogRewardRangeView ParseRewardRange(string eventKey, string where, string raw) =>
        ShopNormalization.TryParseRewardRange(raw, out var type, out var min, out var max)
            ? new GameCatalogRewardRangeView(type, min, max)
            : throw EventShopError(eventKey, $"'{where}' has an unparseable reward '{raw}'.");

    private static GameCatalogEventShopMissionsView BuildMissions(
        string eventKey,
        int week,
        string prefix,
        IReadOnlyDictionary<string, IReadOnlyList<DatamineSeasonalQuest>> seasonalQuests)
    {
        var daily = seasonalQuests[SeasonalDailyQuestGroup]
            .Where(quest => quest.Name.StartsWith($"{prefix}_daily_", StringComparison.Ordinal))
            .OrderBy(quest => quest.Name, StringComparer.Ordinal)
            .Select(quest => BuildMission(eventKey, quest))
            .ToArray();

        var freeChainPattern = new Regex(
            $"^{Regex.Escape(prefix)}_free_(?<chain>[0-9]+)_chain_[0-9]+$", RegexOptions.CultureInvariant);
        var free = seasonalQuests[SeasonalFreeQuestGroup]
            .Select(quest => (Quest: quest, Match: freeChainPattern.Match(quest.Name)))
            .Where(entry => entry.Match.Success)
            .GroupBy(entry => int.Parse(entry.Match.Groups["chain"].Value, CultureInfo.InvariantCulture))
            .OrderBy(group => group.Key)
            .Select(group => BuildChain(eventKey, $"{prefix}_free_{group.Key}", group.Select(entry => entry.Quest).ToArray()))
            .ToArray();

        var premiumChainPattern = new Regex(
            $"^{Regex.Escape(prefix)}_premium_chain_[0-9]+$", RegexOptions.CultureInvariant);
        var premiumQuests = seasonalQuests[SeasonalPremiumQuestGroup]
            .Where(quest => premiumChainPattern.IsMatch(quest.Name))
            .ToArray();
        GameCatalogEventShopMissionChainView[] premium = premiumQuests.Length == 0
            ? []
            : [BuildChain(eventKey, $"{prefix}_premium", premiumQuests)];

        if (daily.Length == 0 && free.Length == 0 && premium.Length == 0)
        {
            throw EventShopError(eventKey, $"week {week} mission prefix '{prefix}' matches no seasonal quest.");
        }

        return new GameCatalogEventShopMissionsView(daily, free, premium);
    }

    /// <summary>Orders a chain's quests by following <c>successorQuests</c> from the one no other quest succeeds.</summary>
    private static GameCatalogEventShopMissionChainView BuildChain(
        string eventKey, string chainId, DatamineSeasonalQuest[] quests)
    {
        var byName = quests.ToDictionary(quest => quest.Name, StringComparer.Ordinal);
        var successors = quests
            .SelectMany(quest => quest.SuccessorQuests ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var heads = quests.Where(quest => !successors.Contains(quest.Name)).ToArray();
        if (heads.Length != 1)
        {
            throw EventShopError(eventKey, $"mission chain '{chainId}' has {heads.Length} starting quests; expected 1.");
        }

        var ordered = new List<DatamineSeasonalQuest>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var current = heads[0]; current is not null && visited.Add(current.Name);)
        {
            ordered.Add(current);
            current = (current.SuccessorQuests ?? [])
                .Select(name => byName.GetValueOrDefault(name))
                .FirstOrDefault(next => next is not null);
        }

        if (ordered.Count != quests.Length)
        {
            throw EventShopError(eventKey, $"mission chain '{chainId}' links {ordered.Count} of its {quests.Length} quests.");
        }

        return new GameCatalogEventShopMissionChainView(chainId, ordered.Select(quest => BuildMission(eventKey, quest)).ToArray());
    }

    private static GameCatalogEventShopMissionView BuildMission(string eventKey, DatamineSeasonalQuest quest) =>
        new(
            quest.Name,
            (quest.Rewards ?? []).Select(reward => ParseRewardRange(eventKey, quest.Name, reward)).ToArray(),
            (quest.Tasks ?? [])
                .Select(task => new GameCatalogEventShopTaskView(
                    task.Name,
                    task.Target,
                    task.TaskParameters is { Count: > 0 } parameters ? parameters : null))
                .ToArray());

    private static InvalidOperationException EventShopError(string eventKey, string message) =>
        new($"GameCatalog dataset '{GameCatalogDatasets.EventShops}' event '{eventKey}': {message}");
}
