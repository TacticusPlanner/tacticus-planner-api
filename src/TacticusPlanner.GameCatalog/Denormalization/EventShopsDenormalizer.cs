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

    [GeneratedRegex(
        "^(?<key>" + ShopEventParameters.EventKeyRegex + ")(?:Week(?<week>[0-9]+))?EventShop$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EventMerchantKeyPattern();

    /// <summary>The rest of a free mission name after <c>&lt;prefix&gt;_free_</c>: <c>&lt;n&gt;_chain_&lt;k&gt;</c>.</summary>
    [GeneratedRegex("^(?<chain>[0-9]+)_chain_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FreeChainSuffixPattern();

    /// <summary>The rest of a premium mission name after <c>&lt;prefix&gt;_premium_</c>: <c>chain_&lt;k&gt;</c>.</summary>
    [GeneratedRegex("^chain_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PremiumChainSuffixPattern();

    /// <summary>
    /// Discovers the seasonal event shops: every <c>liveEventRequired</c> merchant named
    /// <c>&lt;Month&gt;&lt;Year&gt;[Week&lt;N&gt;]EventShop</c>, dated by the calendar's <c>ShopEvent</c> occurrences with
    /// the same <c>eventKey</c>. A calendar key with no merchant is not returned. With per-week merchants, an event is
    /// served up to its first calendar week with no <c>Week&lt;N&gt;</c> merchant (none when week 1 has none). A
    /// merchant key with no calendar week at all is returned undated (start date to be announced: no definition, no
    /// occurrences; its weeks are 1 for a shared merchant, else 1 up to the first missing <c>Week&lt;N&gt;</c>), but
    /// only when its month is not before the month the latest dated event starts in, so an old event the calendar
    /// never dated does not show up as upcoming. None of these fail the load; the CI calendar checks in
    /// <c>ShopEventCalendarTests</c> report the gaps that matter (current or upcoming weeks with no merchant,
    /// merchants with no calendar week). See specs/game-event-shops-dataset.
    /// </summary>
    public static IReadOnlyList<GameCatalogRawEventShop> ReadDatamineEventShops(
        IReadOnlyDictionary<string, JsonElement> merchants,
        IReadOnlyList<GameCatalogEventDefinition> eventDefinitions,
        IReadOnlyList<GameCatalogEventOccurrence> eventOccurrences,
        JsonSerializerOptions options)
    {
        var merchantsByEventKey = DatamineEventMerchants(merchants);

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
                .Select(week => (week.Week, week.Occurrence, MerchantKey: perWeekMerchants
                    ? eventMerchants.PerWeek.GetValueOrDefault(week.Week)
                    : eventMerchants.Shared))
                // Served up to the first calendar week with no merchant: a datamine that ships an event's first
                // weeks before the rest, or drops its last ones, keeps loading. The CI calendar check reports the
                // dropped weeks while they have not ended.
                .TakeWhile(week => week.MerchantKey is not null)
                .Select(week => new GameCatalogRawEventShopWeek(
                    week.Week,
                    week.MerchantKey,
                    merchants[week.MerchantKey!].Deserialize<GameCatalogRawShop>(options),
                    week.Occurrence))
                .ToArray();
            if (rawWeeks.Length == 0)
            {
                continue;
            }

            events.Add(new GameCatalogRawEventShop(
                eventKey, weeks[0].Occurrence.DefinitionId, perWeekMerchants, rawWeeks));
        }

        // Undated events count from the month the latest dated event starts in (any month when none is dated).
        var undatedFrom = events.Count == 0
            ? (DateTimeOffset?)null
            : events.Max(shop => FirstOfMonth(shop.Weeks[0].Occurrence!.StartUtc));
        events.AddRange(ReadUndatedEventShops(merchants, merchantsByEventKey, weeksByEventKey, undatedFrom, options).ToArray());

        // Dated events by start, then the undated ones by month.
        return events
            .OrderBy(shop => shop.IsDated ? 0 : 1)
            .ThenBy(shop => shop.Weeks[0].Occurrence?.StartUtc ?? EventKeyMonth(shop.EventKey))
            .ThenBy(shop => shop.EventKey, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The live-event merchants the calendar has no week for, as undated events, from the month the latest dated
    /// event starts in onwards. See <see cref="ReadDatamineEventShops"/>.
    /// </summary>
    private static IEnumerable<GameCatalogRawEventShop> ReadUndatedEventShops(
        IReadOnlyDictionary<string, JsonElement> merchants,
        IReadOnlyDictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)> merchantsByEventKey,
        IReadOnlyDictionary<string, IReadOnlyList<(int Week, GameCatalogEventOccurrence Occurrence)>> weeksByEventKey,
        DateTimeOffset? earliestMonth,
        JsonSerializerOptions options)
    {
        foreach (var (eventKey, eventMerchants) in merchantsByEventKey)
        {
            if (weeksByEventKey.ContainsKey(eventKey)
                || EventKeyMonth(eventKey) is not { } month
                || (earliestMonth is { } earliest && month < earliest))
            {
                continue;
            }

            var merchantKeys = eventMerchants.PerWeek.Count > 0
                ? Enumerable.Range(1, eventMerchants.PerWeek.Count)
                    .TakeWhile(eventMerchants.PerWeek.ContainsKey)
                    .Select(week => (Week: week, MerchantKey: eventMerchants.PerWeek[week]))
                    .ToArray()
                : [(1, eventMerchants.Shared!)];
            if (merchantKeys.Length == 0)
            {
                continue;
            }

            yield return new GameCatalogRawEventShop(
                eventKey,
                null,
                eventMerchants.PerWeek.Count > 0,
                merchantKeys
                    .Select(week => new GameCatalogRawEventShopWeek(
                        week.Week,
                        week.MerchantKey,
                        merchants[week.MerchantKey].Deserialize<GameCatalogRawShop>(options),
                        null))
                    .ToArray());
        }
    }

    /// <summary>The first instant of an event key's month (<c>November2026</c> → 2026-11-01 UTC), or null when the
    /// key is not a month and year.</summary>
    internal static DateTimeOffset? EventKeyMonth(string eventKey) =>
        DateTimeOffset.TryParseExact(
            eventKey, "MMMMyyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var month)
            ? FirstOfMonth(month)
            : null;

    private static DateTimeOffset FirstOfMonth(DateTimeOffset instant) =>
        new(instant.Year, instant.Month, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The datamine's live-event merchants by event key: the shared <c>&lt;Key&gt;EventShop</c> merchant key, if any,
    /// and the <c>&lt;Key&gt;Week&lt;N&gt;EventShop</c> merchant keys by week number.
    /// </summary>
    internal static IReadOnlyDictionary<string, (string? Shared, SortedDictionary<int, string> PerWeek)> DatamineEventMerchants(
        IReadOnlyDictionary<string, JsonElement> merchants)
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

        return merchantsByEventKey;
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
    /// the seasonal quest groups. A week whose mission prefix matches no seasonal quest serves no missions.
    /// </summary>
    /// <exception cref="InvalidOperationException">A ladder or mission reward cannot be parsed, or a mission chain is
    /// broken.</exception>
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
                week.Occurrence is null ? null : ReadIncome(week.Occurrence),
                week.Merchant is null ? [] : BuildShopSlots(week.Merchant),
                progressChestsKey is null ? [] : BuildRewardTiers(raw.EventKey, progressChestsKey, tieredProgressRewards, chests),
                BuildMissions(raw.EventKey, missionPrefix, seasonalQuests));
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
            raw.Weeks[0].Occurrence?.StartUtc,
            7 * raw.Weeks.Count,
            milestones,
            weeks);
    }

    private static string DatamineEventKeyPrefix(string eventKey, string separator)
    {
        var match = ShopEventParameters.EventKeyPattern().Match(eventKey);
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
        string prefix,
        IReadOnlyDictionary<string, IReadOnlyList<DatamineSeasonalQuest>> seasonalQuests)
    {
        var daily = seasonalQuests[SeasonalDailyQuestGroup]
            .Where(quest => quest.Name.StartsWith($"{prefix}_daily_", StringComparison.Ordinal))
            .OrderBy(quest => quest.Name, StringComparer.Ordinal)
            .Select(quest => BuildMission(eventKey, quest))
            .ToArray();

        var freePrefix = $"{prefix}_free_";
        var free = seasonalQuests[SeasonalFreeQuestGroup]
            .Where(quest => quest.Name.StartsWith(freePrefix, StringComparison.Ordinal))
            .Select(quest => (Quest: quest, Match: FreeChainSuffixPattern().Match(quest.Name[freePrefix.Length..])))
            .Where(entry => entry.Match.Success)
            .GroupBy(entry => int.Parse(entry.Match.Groups["chain"].Value, CultureInfo.InvariantCulture))
            .OrderBy(group => group.Key)
            .Select(group => BuildChain(eventKey, $"{prefix}_free_{group.Key}", group.Select(entry => entry.Quest).ToArray()))
            .ToArray();

        var premiumPrefix = $"{prefix}_premium_";
        var premiumQuests = seasonalQuests[SeasonalPremiumQuestGroup]
            .Where(quest => quest.Name.StartsWith(premiumPrefix, StringComparison.Ordinal)
                && PremiumChainSuffixPattern().IsMatch(quest.Name.AsSpan(premiumPrefix.Length)))
            .ToArray();
        GameCatalogEventShopMissionChainView[] premium = premiumQuests.Length == 0
            ? []
            : [BuildChain(eventKey, $"{prefix}_premium", premiumQuests)];

        // A prefix that matches no quest (a datamine that pruned an old event's quests) serves no missions, like a
        // missing ladder serves no tiers, so one event's quests can't fail the whole catalog load.
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
