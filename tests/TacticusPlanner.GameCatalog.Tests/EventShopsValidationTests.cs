using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class EventShopsValidationTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);

    private static GameCatalogEventOccurrence Occurrence(int week, DateTimeOffset start, TimeSpan? length = null) =>
        new($"occ-june-{week}", "armageddon-shop", start, start + (length ?? TimeSpan.FromDays(7)),
            new Dictionary<string, JsonElement>());

    private static GameCatalogRawShop Merchant(string reward = "gold:1", string currency = "seasonalEventCurrencyJune2026") =>
        new("seasonalEventMerchant", null, false, 0,
        [
            [new GameCatalogRawShopVariant(1, null, "0 0 0 ? * * *", reward, null, "1", new GameCatalogRawShopCost(currency, 1))],
        ]);

    private static GameCatalogRawEventShopWeek Week(int week, DateTimeOffset start, GameCatalogRawShop? merchant = null, TimeSpan? length = null) =>
        new(week, $"June2026Week{week}EventShop", merchant ?? Merchant(), Occurrence(week, start, length));

    private static List<GameCatalogValidationError> Validate(params GameCatalogRawEventShopWeek[] weeks)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateEventShops(
            [new GameCatalogRawEventShop("June2026", "armageddon-shop", true, weeks)],
            [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            errors);
        return errors;
    }

    private static void AssertSingleError(List<GameCatalogValidationError> errors, string code)
    {
        var error = Assert.Single(errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(GameCatalogDatasets.EventShops, error.Dataset);
        Assert.Contains("June2026", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContiguousWeeksWithMerchantsPass() =>
        Assert.Empty(Validate(Week(1, Start), Week(2, Start.AddDays(7)), Week(3, Start.AddDays(14))));

    [Fact]
    public void MissingPerWeekMerchantFails()
    {
        var errors = Validate(Week(1, Start), new GameCatalogRawEventShopWeek(2, null, null, Occurrence(2, Start.AddDays(7))));

        AssertSingleError(errors, "MissingEventShopMerchant");
        Assert.Contains("week 2", errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WeekNumbersNotOneToWFail() =>
        AssertSingleError(Validate(Week(1, Start), Week(3, Start.AddDays(7))), "InvalidEventShopWeeks");

    [Fact]
    public void DuplicateWeekNumberFails()
    {
        var errors = Validate(Week(1, Start), Week(1, Start.AddDays(7)));

        Assert.Contains(errors, error => error.Code == "InvalidEventShopWeeks");
    }

    [Fact]
    public void WeekThatIsNotSevenDaysFails() =>
        AssertSingleError(Validate(Week(1, Start, length: TimeSpan.FromDays(6))), "InvalidEventShopWeek");

    [Fact]
    public void WeekNotStartingAtMidnightFails() =>
        AssertSingleError(Validate(Week(1, Start.AddHours(8))), "InvalidEventShopWeek");

    [Fact]
    public void GapBetweenWeeksFails() =>
        AssertSingleError(Validate(Week(1, Start), Week(2, Start.AddDays(8))), "InvalidEventShopWeeks");

    [Fact]
    public void OverlappingWeeksFail() =>
        AssertSingleError(Validate(Week(1, Start), Week(2, Start.AddDays(6))), "InvalidEventShopWeeks");

    [Fact]
    public void VariantCostingAnotherCurrencyFails() =>
        AssertSingleError(Validate(Week(1, Start, Merchant(currency: "gems"))), "EventShopCurrencyMismatch");

    [Fact]
    public void VariantFailingADailyShopRuleFails() =>
        AssertSingleError(Validate(Week(1, Start, Merchant(reward: "shards_x:0"))), "UnparseableReward");

    [Fact]
    public void ServedSlotWithUnresolvableShardUnitFails()
    {
        var view = new GameCatalogEventShopView("june-2026", "June2026", "armageddon-shop", "seasonalEventCurrencyJune2026",
            Start, 7, [],
            [
                new GameCatalogEventShopWeekView(1, null,
                [
                    new GameCatalogShopSlotView(
                    [
                        new GameCatalogShopVariantView(new GameCatalogShopRewardView("shards_nobody", 5), "nobody", null,
                            new GameCatalogShopCostView("seasonalEventCurrencyJune2026", 45), 5, null, ["MON"], null, null, null),
                    ]),
                ], [], new GameCatalogEventShopMissionsView([], [], [])),
            ]);
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateEventShops([], [view], new HashSet<string>(StringComparer.OrdinalIgnoreCase), errors);

        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.EventShops, error.Dataset);
        Assert.Contains("June2026 week 1", error.Message, StringComparison.Ordinal);
    }
}
