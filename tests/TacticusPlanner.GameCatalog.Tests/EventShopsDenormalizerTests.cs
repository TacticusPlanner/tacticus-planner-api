using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>The served <c>event-shops</c> dataset over the pinned datamine and the seeded calendar weeks.</summary>
public sealed class EventShopsDenormalizerTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();

    private static GameCatalogEventShopView Event(string id) =>
        Assert.Single(Snapshot.EventShopViews, eventShop => eventShop.Id == id);

    [Fact]
    public void ServesTheFourDatedEventsInStartOrder()
    {
        Assert.Equal(["june-2026", "august-2026", "october-2026", "november-2026"], Snapshot.EventShopViews.Select(view => view.Id));
        Assert.Equal(["June2026", "August2026", "October2026", "November2026"], Snapshot.EventShopViews.Select(view => view.EventKey));
    }

    [Fact]
    public void DatesAndDayCountComeFromTheCalendarWeeks()
    {
        var june = Event("june-2026");
        var august = Event("august-2026");
        var october = Event("october-2026");

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), june.StartUtc);
        Assert.Equal(21, june.DayCount);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), august.StartUtc);
        Assert.Equal(14, august.DayCount);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), october.StartUtc);
        Assert.Equal(7, october.DayCount);
        Assert.Equal("armageddon-shop", june.DefinitionId);
        Assert.Equal("anniversary-event", august.DefinitionId);
        Assert.Equal("seasonalEventCurrencyOctober2026", october.Currency);

        // Skull Harvest: begins Monday 23 November and ends with the Blood Tithe HSE on 29 November 00:00 UTC
        // (the datamine's november_2026 profile theme window).
        var november = Event("november-2026");
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero), november.StartUtc);
        Assert.Equal(6, november.DayCount);
        Assert.Equal("skull-harvest-shop", november.DefinitionId);
    }

    [Fact]
    public void PerWeekMerchantsLaddersAndMissionsFollowTheWeekSuffix()
    {
        var week2 = Event("june-2026").Weeks[1];
        var merchant = Assert.Single(Snapshot.RawEventShops, shop => shop.EventKey == "June2026").Weeks[1];

        Assert.Equal(2, week2.Week);
        Assert.Equal("June2026Week2EventShop", merchant.MerchantKey);
        Assert.Equal(merchant.Merchant!.Products.Count, week2.Slots.Count);
        Assert.Equal(20, week2.ProgressChests.Count);
        Assert.Equal("june_2026_week2_free_1", Assert.Single(week2.Missions.Free).Id);
        Assert.Equal("june_2026_week2_premium", Assert.Single(week2.Missions.Premium).Id);
        Assert.All(week2.Missions.Daily, mission => Assert.StartsWith("june_2026_week2_daily_", mission.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void SharedMerchantServesEveryWeekWithTheUnsuffixedSources()
    {
        var week1 = Assert.Single(Event("october-2026").Weeks);

        Assert.Equal("October2026EventShop", Assert.Single(Snapshot.RawEventShops, shop => shop.EventKey == "October2026").Weeks[0].MerchantKey);
        Assert.Equal(24, week1.Slots.Count);
        Assert.Equal(20, week1.ProgressChests.Count);
        Assert.Equal("october_2026_free_1", Assert.Single(week1.Missions.Free).Id);
        Assert.NotEmpty(week1.Missions.Daily);
    }

    [Fact]
    public void YarrickVariantUsesTheDailyShopProjection()
    {
        var yarrick = Assert.Single(
            Event("october-2026").Weeks[0].Slots[1].Variants,
            variant => variant.Reward.Type == "shards_astraYarrick" && variant.LockId == "lock_below_max_legendary_astraYarrick");

        Assert.Equal(25, yarrick.Reward.Qty);
        Assert.Equal("astraYarrick", yarrick.UnitId);
        Assert.Equal(new GameCatalogShopCostView("seasonalEventCurrencyOctober2026", 45), yarrick.Cost);
        Assert.Equal(5, yarrick.MaxPurchasesPerDay);
        Assert.Equal(["MON", "THU", "SUN"], yarrick.Days);
    }

    [Fact]
    public void ChestBackedTierListsEveryChestReward()
    {
        var tier = Event("october-2026").Weeks[0].ProgressChests[0];

        Assert.Equal(30, tier.RequiredProgress);
        Assert.Equal(
            [new GameCatalogRewardRangeView("gold", 600, 660), new GameCatalogRewardRangeView("shards_astraYarrick", 5, 5)],
            tier.Rewards);
    }

    [Fact]
    public void DirectRewardTierParsesTheChestRewardId()
    {
        var tier = Event("october-2026").Milestones[0];

        Assert.Equal(100, tier.RequiredProgress);
        Assert.Equal([new GameCatalogRewardRangeView("seasonalEventCurrencyOctober2026", 15, 15)], tier.Rewards);
    }

    [Fact]
    public void FreeChainIsOrderedBySuccessor()
    {
        var chain = Assert.Single(Event("october-2026").Weeks[0].Missions.Free);

        Assert.Equal(20, chain.Missions.Count);
        Assert.Equal(
            Enumerable.Range(1, 20).Select(index => $"october_2026_free_1_chain_{index:00}"),
            chain.Missions.Select(mission => mission.Id));
    }

    [Fact]
    public void TaskParametersPassThroughAsShipped()
    {
        var daily = Event("june-2026").Weeks.SelectMany(week => week.Missions.Daily).ToDictionary(mission => mission.Id);

        var scalar = Assert.Single(daily["june_2026_week1_daily_04"].Tasks, task => task.Parameters is not null);
        Assert.Equal("LinearHeroEvent", scalar.Parameters!["gameMode"].GetString());

        var list = Assert.Single(daily["june_2026_week2_daily_05"].Tasks, task => task.Parameters is not null);
        Assert.Equal(["AllCampaigns"], list.Parameters!["gameModes"].EnumerateArray().Select(mode => mode.GetString()));
    }

    [Fact]
    public void IncomeComesFromEachCalendarWeek()
    {
        Assert.All(Event("august-2026").Weeks, week =>
            Assert.Equal(new GameCatalogEventShopIncomeView(750, 1000), week.EstimatedWeeklyIncome));
        Assert.Null(Event("october-2026").Weeks[0].EstimatedWeeklyIncome);
    }

    [Fact]
    public void ServedJsonHasTheSpecShapeAndOmitsAbsentOptionals()
    {
        var json = JsonSerializer.SerializeToElement(Event("october-2026"), WebOptions);

        Assert.Equal(
            ["id", "eventKey", "definitionId", "currency", "startUtc", "dayCount", "milestones", "weeks"],
            json.EnumerateObject().Select(property => property.Name));
        var week = json.GetProperty("weeks")[0];
        Assert.False(week.TryGetProperty("estimatedWeeklyIncome", out _));
        Assert.Equal(["week", "slots", "progressChests", "missions"], week.EnumerateObject().Select(property => property.Name));
        var task = week.GetProperty("missions").GetProperty("daily")[0].GetProperty("tasks")[0];
        Assert.True(task.TryGetProperty("taskId", out _));
        Assert.True(task.TryGetProperty("target", out _));
    }

    // ---- discovery rules over synthetic inputs ------------------------------------------------------

    private static readonly GameCatalogEventDefinition ShopEventDefinition =
        new("armageddon-shop", "ShopEvent", new GameCatalogEventRecurrence("None", null, null, null), ["weekNumber", "eventKey"], null);

    private static GameCatalogEventOccurrence Week(string eventKey, int week, DateTimeOffset start) =>
        new($"occ-{eventKey}-{week}", "armageddon-shop", start, start.AddDays(7), new Dictionary<string, JsonElement>
        {
            ["weekNumber"] = JsonSerializer.SerializeToElement(week),
            ["eventKey"] = JsonSerializer.SerializeToElement(eventKey),
        });

    private static JsonElement Merchant(bool liveEventRequired = true) => JsonSerializer.SerializeToElement(new
    {
        displayLocation = "seasonalEventMerchant",
        refreshWithAdWatch = false,
        allowedRefreshesPerDay = 0,
        liveEventRequired,
        products = Array.Empty<object>(),
    });

    private static readonly DateTimeOffset Start = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] OneGold = ["gold:1"];

    [Fact]
    public void UndatedSharedMerchantIsServedAsOneWeekWithoutADate()
    {
        var undated = Assert.Single(GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement> { ["November2026EventShop"] = Merchant() },
            [ShopEventDefinition], [], WebOptions));

        Assert.False(undated.IsDated);
        Assert.Null(undated.DefinitionId);
        var week = Assert.Single(undated.Weeks);
        Assert.Equal(1, week.Week);
        Assert.Equal("November2026EventShop", week.MerchantKey);
        Assert.Null(week.Occurrence);
    }

    [Fact]
    public void UndatedPerWeekMerchantsServeWeeksUpToTheFirstMissingOne()
    {
        var undated = Assert.Single(GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement>
            {
                ["November2026Week1EventShop"] = Merchant(),
                ["November2026Week2EventShop"] = Merchant(),
                ["November2026Week4EventShop"] = Merchant(),
            },
            [ShopEventDefinition], [], WebOptions));

        Assert.Equal([1, 2], undated.Weeks.Select(week => week.Week));
        Assert.True(undated.PerWeekMerchants);
    }

    [Fact]
    public void UndatedEventsFollowTheDatedOnesAndOlderOnesAreDropped()
    {
        var events = GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement>
            {
                ["December2026EventShop"] = Merchant(),
                ["November2026EventShop"] = Merchant(),
                ["October2026EventShop"] = Merchant(),
                ["May2026EventShop"] = Merchant(),
            },
            [ShopEventDefinition], [Week("November2026", 1, Start)], WebOptions);

        // November is dated; December is undated and later; October and May are undated and before November's
        // month (an old event the calendar never dated), so they are not served.
        Assert.Equal(["November2026", "December2026"], events.Select(shop => shop.EventKey));
        Assert.Equal([true, false], events.Select(shop => shop.IsDated));
    }

    [Fact]
    public void UndatedEventServesNoStartNoDefinitionAndNoIncome()
    {
        var raw = new GameCatalogRawEventShop("December2026", null, false,
        [
            new GameCatalogRawEventShopWeek(1, "December2026EventShop",
                new GameCatalogRawShop("seasonalEventMerchant", null, false, 0, []), null),
        ]);

        var served = Assert.Single(GameCatalogDenormalizer.BuildEventShops(
            [raw], new Dictionary<string, List<DatamineTieredProgressReward>>(), new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(), WebOptions));

        Assert.Equal("december-2026", served.Id);
        Assert.Null(served.StartUtc);
        Assert.Null(served.DefinitionId);
        Assert.Equal(7, served.DayCount);
        Assert.Null(Assert.Single(served.Weeks).EstimatedWeeklyIncome);
    }

    [Fact]
    public void CalendarWeekWithoutAnyMerchantIsSkippedWithoutFailing()
    {
        var events = GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement>(), [ShopEventDefinition], [Week("November2026", 1, Start)], WebOptions);

        Assert.Empty(events);
    }

    [Fact]
    public void MerchantsThatAreNotLiveEventsAreIgnored()
    {
        var events = GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement> { ["November2026EventShop"] = Merchant(liveEventRequired: false) },
            [ShopEventDefinition], [Week("November2026", 1, Start)], WebOptions);

        Assert.Empty(events);
    }

    [Fact]
    public void SharedMerchantIsUsedForEveryWeek()
    {
        var shared = Assert.Single(GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement> { ["November2026EventShop"] = Merchant() },
            [ShopEventDefinition], [Week("November2026", 1, Start), Week("November2026", 2, Start.AddDays(7))], WebOptions));

        Assert.All(shared.Weeks, week => Assert.Equal("November2026EventShop", week.MerchantKey));
    }

    [Fact]
    public void PerWeekMerchantsServeUpToTheFirstCalendarWeekWithoutOne()
    {
        var perWeek = Assert.Single(GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement>
            {
                ["November2026Week1EventShop"] = Merchant(),
                ["November2026Week3EventShop"] = Merchant(),
            },
            [ShopEventDefinition],
            [Week("November2026", 1, Start), Week("November2026", 2, Start.AddDays(7)), Week("November2026", 3, Start.AddDays(14))],
            WebOptions));

        var week = Assert.Single(perWeek.Weeks);
        Assert.Equal(1, week.Week);
        Assert.Equal("November2026Week1EventShop", week.MerchantKey);
    }

    [Fact]
    public void PerWeekMerchantsWithoutAWeekOneMerchantServeNothing()
    {
        var events = GameCatalogDenormalizer.ReadDatamineEventShops(
            new Dictionary<string, JsonElement> { ["November2026Week2EventShop"] = Merchant() },
            [ShopEventDefinition], [Week("November2026", 1, Start), Week("November2026", 2, Start.AddDays(7))], WebOptions);

        Assert.Empty(events);
    }

    [Fact]
    public void UnmatchedMissionPrefixServesNoMissions()
    {
        var raw = new GameCatalogRawEventShop("November2026", "armageddon-shop", false,
        [
            new GameCatalogRawEventShopWeek(1, "November2026EventShop",
                new GameCatalogRawShop("seasonalEventMerchant", null, false, 0, []), Week("November2026", 1, Start)),
        ]);

        var served = Assert.Single(GameCatalogDenormalizer.BuildEventShops(
            [raw], new Dictionary<string, List<DatamineTieredProgressReward>>(), new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(), WebOptions));

        var missions = Assert.Single(served.Weeks).Missions;
        Assert.Empty(missions.Daily);
        Assert.Empty(missions.Free);
        Assert.Empty(missions.Premium);
    }

    [Fact]
    public void UnparseableLadderRewardFailsTheLoadNamingTheEvent()
    {
        var raw = new GameCatalogRawEventShop("November2026", "armageddon-shop", false,
        [
            new GameCatalogRawEventShopWeek(1, "November2026EventShop",
                new GameCatalogRawShop("seasonalEventMerchant", null, false, 0, []), Week("November2026", 1, Start)),
        ]);
        var questGroups = new Dictionary<string, JsonElement>
        {
            ["seasonal_daily"] = JsonSerializer.SerializeToElement(new
            {
                quests = new[] { new { name = "november_2026_daily_01", rewards = OneGold, tasks = Array.Empty<object>() } },
            }),
        };
        var ladders = new Dictionary<string, List<DatamineTieredProgressReward>>
        {
            ["se_milestones_november_2026"] = [new DatamineTieredProgressReward(100, "gold:9-1", null, null)],
        };

        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.BuildEventShops(
            [raw], ladders, new Dictionary<string, JsonElement>(), questGroups, WebOptions));

        Assert.Contains("November2026", error.Message, StringComparison.Ordinal);
        Assert.Contains("gold:9-1", error.Message, StringComparison.Ordinal);
    }
}
