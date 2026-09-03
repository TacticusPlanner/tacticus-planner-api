using TacticusPlanner.GameCatalog.Utils;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class ShopNormalizationTests
{
    [Theory]
    [InlineData("shards_eldarFarseer:5", "shards_eldarFarseer", 5)]
    [InlineData("draft_machinesOfWarTokens:10", "draft_machinesOfWarTokens", 10)]
    [InlineData("itemAscensionResource_Mythic", "itemAscensionResource_Mythic", 1)]
    [InlineData("xpMythic", "xpMythic", 1)]
    public void TryParseTypedQuantityParsesTypeAndQuantity(string raw, string expectedType, int expectedQty)
    {
        Assert.True(ShopNormalization.TryParseTypedQuantity(raw, out var type, out var qty));
        Assert.Equal(expectedType, type);
        Assert.Equal(expectedQty, qty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":5")]
    [InlineData("shards_x:0")]
    [InlineData("shards_x:-2")]
    [InlineData("shards_x:abc")]
    [InlineData("shards_x:2.5")]
    public void TryParseTypedQuantityRejectsMalformedStrings(string? raw)
    {
        Assert.False(ShopNormalization.TryParseTypedQuantity(raw, out _, out _));
    }

    [Fact]
    public void ReduceCronToDaysKeepsAnExplicitDayRestrictionInWeekdayOrder()
    {
        Assert.Equal(["MON", "THU"], ShopNormalization.ReduceCronToDays("0 0 0 ? * MON,THU *"));
        // Source token order does not matter — the list is always weekday-ordered.
        Assert.Equal(["WED", "SAT", "SUN"], ShopNormalization.ReduceCronToDays("0 0 0 ? * SUN,WED,SAT *"));
    }

    [Theory]
    [InlineData("0 0 0 ? * * *")]
    [InlineData("0 0 0 * * ? *")]
    public void ReduceCronToDaysExpandsAnUnrestrictedCronToAllSevenDays(string cron)
    {
        Assert.Equal(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"], ShopNormalization.ReduceCronToDays(cron));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 0 0 ? *")]
    [InlineData("0 0 0 ? * BADTOKEN *")]
    [InlineData("0 0 0 ? * 1-5 *")]
    // A genuine time-of-day / day-of-month / month restriction is not representable as a plain day list —
    // reducing it would silently drop the restriction, so it must yield empty (and fail validation).
    [InlineData("0 0 12 ? * MON *")]
    [InlineData("30 0 0 ? * MON *")]
    [InlineData("0 0 0 15 * ? *")]
    [InlineData("0 0 0 ? 6 MON *")]
    public void ReduceCronToDaysYieldsEmptyForUnreducibleOrGarbageCrons(string cron)
    {
        Assert.Empty(ShopNormalization.ReduceCronToDays(cron));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("2", 2)]
    [InlineData("15", 15)]
    public void ParseMaxPurchasesPerDayDefaultsToOne(string? raw, int expected)
    {
        Assert.Equal(expected, ShopNormalization.ParseMaxPurchasesPerDay(raw));
    }

    [Theory]
    [InlineData("shards_eldarFarseer", "eldarFarseer")]
    [InlineData("mythicShards_eldarLhykhis", "eldarLhykhis")]
    [InlineData("gold", null)]
    [InlineData("upgHpM001", null)]
    [InlineData("shards_", null)]
    public void ShardUnitIdExtractsOnlyFromShardRewardTypes(string rewardType, string? expected)
    {
        Assert.Equal(expected, ShopNormalization.ShardUnitId(rewardType));
    }

    [Theory]
    [InlineData("guildCredits", 525, true)]
    [InlineData("guildCredits", 0, true)]
    [InlineData("", 525, false)]
    [InlineData(null, 525, false)]
    [InlineData("guildCredits", -1, false)]
    [InlineData("guildCredits", double.NaN, false)]
    public void IsParseableCostRequiresCurrencyAndNonNegativeAmount(string? currency, double amount, bool expected)
    {
        Assert.Equal(expected, ShopNormalization.IsParseableCost(currency, amount));
    }
}
