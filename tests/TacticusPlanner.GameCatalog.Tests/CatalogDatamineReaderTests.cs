using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class CatalogDatamineReaderTests
{
    private static GameCatalogRaidBossRawUnitSet UnitSet(int health) =>
        new("Tyranids", 4, null, null,
            [new GameCatalogRaidBossRawStat(health, 25, 10, 0, 0, "Common", 0, 1, null, null, null, null, null)],
            null, null, null, null, null);

    private static DatamineQuest Quest(string name, params string[] rewards) =>
        new(name, rewards, [new GameCatalogRawMythicQuestTask("WinBattlesWithHero_description", 1, new Dictionary<string, string>())]);

    [Fact]
    public void IdenticalRaidBossUnitSetsMergeInGameOrder()
    {
        var merged = GameCatalogDenormalizer.ReadDatamineRaidBossUnitSets(
            new Dictionary<string, Dictionary<string, GameCatalogRaidBossRawUnitSet>>
            {
                ["unit_set_1"] = new() { ["GuildBoss1Boss1A"] = UnitSet(100), ["GuildBoss1Npc1B"] = UnitSet(10) },
                ["unit_set_2"] = new() { ["GuildBoss1Boss1A"] = UnitSet(100), ["GuildBoss2Boss1C"] = UnitSet(200) },
            });

        Assert.Equal(["GuildBoss1Boss1A", "GuildBoss1Npc1B", "GuildBoss2Boss1C"], merged.Keys);
    }

    [Fact]
    public void ARaidBossUnitSetThatDiffersBetweenSetsFailsNamingIt()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineRaidBossUnitSets(
            new Dictionary<string, Dictionary<string, GameCatalogRaidBossRawUnitSet>>
            {
                ["unit_set_1"] = new() { ["GuildBoss1Boss1A"] = UnitSet(100) },
                ["unit_set_2"] = new() { ["GuildBoss1Boss1A"] = UnitSet(101) },
            }));

        Assert.Contains("raid-bosses", error.Message, StringComparison.Ordinal);
        Assert.Contains("GuildBoss1Boss1A", error.Message, StringComparison.Ordinal);
        Assert.Contains("unit_set_2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFourAlwaysOnMerchantsBecomeTheShopSources()
    {
        static JsonElement Merchant(string location) => JsonSerializer.SerializeToElement(new
        {
            displayLocation = location,
            refreshWithAdWatch = true,
            allowedRefreshesPerDay = 1,
            products = Array.Empty<object>(),
        });

        var shops = GameCatalogDenormalizer.ReadDatamineShops(
            new Dictionary<string, JsonElement>
            {
                // The default template carries nulls that the shop record cannot bind; it must be skipped.
                ["default"] = JsonSerializer.SerializeToElement(new { allowedRefreshesPerDay = (int?)null }),
                ["guild"] = Merchant("guildMerchant"),
                ["guildWars"] = Merchant("guildWarsShopMerchant"),
                ["elderShop"] = Merchant("elderShopMerchant"),
                ["crusadeShop"] = Merchant("crusadeShopMerchant"),
                ["October2026EventShop"] = Merchant("eventShop"),
            },
            JsonSerializerOptions.Web);

        Assert.Equal(GameCatalogDatasets.ShopSources, shops.Keys);
        Assert.Equal("guildWarsShopMerchant", shops["shops-war"].DisplayLocation);
        Assert.Equal("elderShopMerchant", shops["shops-rogue-trader"].DisplayLocation);
    }

    [Fact]
    public void AMissingMerchantFailsNamingTheShop()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineShops(
            new Dictionary<string, JsonElement>(), JsonSerializerOptions.Web));

        Assert.Contains("shops", error.Message, StringComparison.Ordinal);
        Assert.Contains("'guild'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MythicQuestsAreGroupedByCharacterFromTheirNames()
    {
        var quests = GameCatalogDenormalizer.ReadDatamineMythicQuests(new DatamineQuestGroup(
        [
            Quest("hero_blackAbaddon_01", "shards_blackAbaddon:10"),
            Quest("mythic_blackAbaddon_02", "gold:250000"),
            Quest("mythic_blackAbaddon_01", "mythicShards_blackAbaddon:4"),
            Quest("mythic_orks_Weirdboy_01", "xpMythic:1"),
        ]));

        Assert.Equal(["blackAbaddon", "orks_Weirdboy"], quests.Select(character => character.CharacterId));
        Assert.Equal([2, 1], quests[0].Quests.Select(quest => quest.Number));
        Assert.Equal(["mythicShards_blackAbaddon:4"], quests[0].Quests[1].Rewards);
    }

    [Fact]
    public void AMythicQuestWithoutANumberFails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineMythicQuests(
            new DatamineQuestGroup([Quest("mythic_blackAbaddon")])));

        Assert.Contains("mythic-quests", error.Message, StringComparison.Ordinal);
        Assert.Contains("mythic_blackAbaddon", error.Message, StringComparison.Ordinal);
    }
}
