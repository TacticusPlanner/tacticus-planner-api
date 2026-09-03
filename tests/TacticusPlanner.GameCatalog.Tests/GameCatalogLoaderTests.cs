using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class GameCatalogLoaderTests
{
    [Fact]
    public void AscensionCostsCoverEveryProgressionStepInLadderOrder()
    {
        var snapshot = GameCatalogLoader.Load();

        // The 20-step (rarity, stars) ascension ladder, ported 1:1 from V1's OrbAscensionCalculator
        // (mirrors the client's `packages/game-domain` progressionOrder ladder).
        string[] expectedOrder =
        [
            "Common:None", "Common:OneStar", "Common:TwoStars",
            "Uncommon:TwoStars", "Uncommon:ThreeStars", "Uncommon:FourStars",
            "Rare:FourStars", "Rare:FiveStars", "Rare:RedOneStar",
            "Epic:RedOneStar", "Epic:RedTwoStars", "Epic:RedThreeStars",
            "Legendary:RedThreeStars", "Legendary:RedFourStars", "Legendary:RedFiveStars", "Legendary:OneBlueStar",
            "Mythic:OneBlueStar", "Mythic:TwoBlueStars", "Mythic:ThreeBlueStars", "Mythic:MythicWings",
        ];

        Assert.Equal(expectedOrder, snapshot.AscensionCostViews.Select(cost => cost.Progression));

        var firstStep = snapshot.AscensionCostViews.Single(cost => cost.Progression == "Common:None");
        Assert.Equal(0, firstStep.Shards);
        Assert.Equal(0, firstStep.Orbs);
        Assert.Null(firstStep.OrbRarity);

        var lastStep = snapshot.AscensionCostViews.Single(cost => cost.Progression == "Mythic:MythicWings");
        Assert.Equal(100, lastStep.MythicShards);
        Assert.Equal(25, lastStep.Orbs);
        Assert.Equal("Mythic", lastStep.OrbRarity);
    }

    [Fact]
    public void UnlockShardCostsCoverEveryRarity()
    {
        var snapshot = GameCatalogLoader.Load();

        var byRarity = snapshot.UnlockShardCostViews.ToDictionary(cost => cost.Rarity, cost => cost.Shards);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Common"] = 40,
                ["Uncommon"] = 80,
                ["Rare"] = 130,
                ["Epic"] = 250,
                ["Legendary"] = 500,
                ["Mythic"] = 1400,
            },
            byRarity);
    }

    [Fact]
    public void OnslaughtRewardsCoverAllProgressCombinationsAndExposeMidpoints()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal(21, snapshot.OnslaughtRewards.Count);
        Assert.All(snapshot.OnslaughtRewards, reward =>
        {
            Assert.InRange(reward.Tier, 1, 3);
            Assert.Equal(5, reward.Regular.Count);
        });
        Assert.Equal(15, snapshot.OnslaughtReward("Gold", 1, "Legendary", mythicShards: false).Midpoint);
        Assert.Equal(2.5, snapshot.OnslaughtReward("Adamantine", 3, "Mythic", mythicShards: true).Midpoint);
    }

    [Theory]
    [InlineData("astraCreed", "FoCE40")]
    [InlineData("eldarMauganRa", "SHME40")]
    public void EliteShardLocationsCombineGuaranteedAndPotentialRewards(string characterId, string battleId)
    {
        var snapshot = GameCatalogLoader.Load();

        var character = snapshot.CharacterViews.Single(character => character.Id == characterId);
        var location = Assert.Single(character.ShardLocations, location => location.BattleId == battleId);

        Assert.True(location.Guaranteed);
        Assert.Null(location.ChanceId);
        Assert.Null(location.Numerator);
        Assert.Null(location.Denominator);
        var effectiveRate = Assert.IsType<double>(location.EffectiveRate);
        Assert.Equal(1.079, effectiveRate, 3);
    }

    [Fact]
    public void EventDefinitionsIncludeFactionBoostAndFocusAsDistinctDefinitions()
    {
        var snapshot = GameCatalogLoader.Load();

        var factionBoost = snapshot.EventDefinitionViews.Single(definition => definition.Id == "hse-faction-boost");
        var factionFocus = snapshot.EventDefinitionViews.Single(definition => definition.Id == "hse-faction-focus");

        Assert.Equal(["targetFactionId"], factionBoost.RequiredParameters);
        Assert.Empty(factionFocus.RequiredParameters);
    }

    [Fact]
    public void EventsCalendarIncludesBothConfirmedAndProjectedEntries()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.NotEmpty(snapshot.EventsCalendar);

        var allEntries = snapshot.EventsCalendar.Values.SelectMany(entries => entries).ToArray();
        Assert.Contains(allEntries, entry => entry.Confirmed && entry.OccurrenceId is not null);
        Assert.Contains(allEntries, entry => !entry.Confirmed && entry.OccurrenceId is null);
    }

    [Fact]
    public void ShopsDatasetHasExactlyTheFourAlwaysOnDailyShops()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal(
            ["guild", "war", "rogue-trader", "crusade"],
            snapshot.ShopViews.Select(shop => shop.Id));
    }

    [Fact]
    public void EveryShopVariantIsFullyNormalizedAndCrossReferenced()
    {
        var snapshot = GameCatalogLoader.Load();
        var unitOrMowIds = new HashSet<string>(
            snapshot.Characters.Select(character => character.Id), StringComparer.OrdinalIgnoreCase);
        unitOrMowIds.UnionWith(snapshot.Mows.Select(mow => mow.Id));

        var variants = snapshot.ShopViews
            .SelectMany(shop => shop.Slots)
            .SelectMany(slot => slot.Variants)
            .ToArray();

        Assert.NotEmpty(variants);
        Assert.All(variants, variant =>
        {
            Assert.NotEmpty(variant.Days);
            Assert.True(variant.Reward.Qty >= 1);
            Assert.False(string.IsNullOrWhiteSpace(variant.Cost.Currency));
            Assert.True(variant.MaxPurchasesPerDay >= 1);

            var isShard = variant.Reward.Type.StartsWith("shards_", StringComparison.Ordinal)
                || variant.Reward.Type.StartsWith("mythicShards_", StringComparison.Ordinal);
            if (isShard)
            {
                Assert.NotNull(variant.UnitId);
                Assert.Contains(variant.UnitId!, unitOrMowIds);
            }
            else
            {
                Assert.Null(variant.UnitId);
            }
        });

        // The Rogue Trader shard rotation is the largest; prove the shard cross-reference actually fired.
        Assert.Contains(variants, variant => variant.UnitId is not null);
    }

    [Fact]
    public void GuildShopRoundTripsRefreshMetadataAndAKnownDayRestrictedShardSlot()
    {
        var snapshot = GameCatalogLoader.Load();

        var guild = snapshot.ShopViews.Single(shop => shop.Id == "guild");
        Assert.Equal("guildMerchant", guild.DisplayLocation);
        Assert.True(guild.RefreshWithAdWatch);
        Assert.Equal(1, guild.AllowedRefreshesPerDay);
        Assert.Equal(new Models.GameCatalogShopRefreshCostView("gems", 50), guild.RefreshCost);

        // Guild slot 4 (0-based 3) rotates space-wolf shards MON,THU / TUE,FRI / WED,SAT with a SUN pair.
        var spaceWulfen = guild.Slots[3].Variants.Single(variant => variant.Reward.Type == "shards_spaceWulfen");
        Assert.Equal(["MON", "THU"], spaceWulfen.Days);
        Assert.Equal(5, spaceWulfen.Reward.Qty);
        Assert.Equal(2, spaceWulfen.MaxPurchasesPerDay);
        Assert.Equal("spaceWulfen", spaceWulfen.UnitId);
        Assert.Equal(new Models.GameCatalogShopCostView("guildCredits", 525), spaceWulfen.Cost);
    }
}
