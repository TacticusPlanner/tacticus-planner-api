using TacticusPlanner.GameCatalog.Models;
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
    public void RaidBossesDatasetSplitsBossesAndPrimesAndPreservesTheSeasonRotation()
    {
        var snapshot = GameCatalogLoader.Load();
        var view = snapshot.RaidBossesView;

        Assert.NotEmpty(view.Bosses);
        Assert.NotEmpty(view.Primes);
        Assert.All(view.Bosses, boss => Assert.Equal("boss", boss.Kind));
        Assert.All(view.Primes, prime => Assert.Equal("prime", prime.Kind));

        Assert.Equal(
            [
                "guild_boss_season_config_1", "guild_boss_season_config_2", "guild_boss_season_config_3",
                "guild_boss_season_config_4", "guild_boss_season_config_5",
            ],
            view.SeasonConfigRotation);
        Assert.All(view.SeasonConfigRotation, id => Assert.True(view.Seasons.ContainsKey(id)));

        // Ghazghkull is a plain boss; Mortarion is a boss flagged as a primarch. Loot objects and field
        // npcs are referenced by encounters but never surface as top-level bosses/primes.
        Assert.Contains(view.Bosses, boss => boss.UnitSetId == "GuildBoss4Boss1OrksGhazghkull" && !boss.IsPrimarch);
        Assert.Contains(view.Bosses, boss => boss.UnitSetId == "GuildBoss5Boss1DeathMortarion" && boss.IsPrimarch);
        Assert.DoesNotContain(view.Bosses.Concat(view.Primes), unit => unit.UnitSetId.Contains("LootObj"));

        // The raw unit sets are authored one file per boss (raid-boss-{n}-{type}.json) and merged at load: the
        // first and last boss files both have to land, in boss-number order.
        Assert.StartsWith("GuildBoss1Boss", view.Bosses[0].UnitSetId, StringComparison.Ordinal);
        Assert.Contains(view.Bosses, boss => boss.UnitSetId == "GuildBoss12Boss1DarkaLion");
    }

    [Fact]
    public void GuildRaidMetaProvidesCuratedTeamsAndV1CompGuidance()
    {
        var meta = GameCatalogLoader.Load().GuildRaidMetaView;

        Assert.Equal("terminus-maximus-guild-raid-boss-meta", meta.SourceId);
        Assert.Equal("2026-07-01", meta.UpdatedOn);
        Assert.Equal(["AdMech", "Battlesuits", "Custodes", "Laviscus", "Multi-Hit", "Neuro", "Z'Kar"], meta.Comps.Select(comp => comp.Id));
        Assert.Equal(12, meta.Bosses.Count);

        // Exact V1 Comp parity: the API data is intentionally static guidance, not a personalized
        // guild roster or team builder. Keep all ordered signature/core/flex/MoW memberships explicit.
        var expectedComps = new[]
        {
            ("AdMech", "admecRuststalker", new[] { "admecRuststalker", "admecMarshall", "admecManipulus" }, new[] { "admecDominus", "orksWarboss", "tauMarksman", "tauCrisis", "eldarAutarch", "votanMemnyr", "custoAtlacoya", "custoTrajann", "templHelbrecht", "necroSpyder", "necroOverlord" }, new[] { "ultraDreadnought", "tyranBiovore", "deathCrawler" }),
            ("Battlesuits", "tauCrisis", new[] { "tauCrisis", "tauFarsight", "admecManipulus" }, new[] { "tauDarkstrider", "tauShadowsun", "eldarFarseer", "eldarLhykhis" }, new[] { "tauBroadside", "tyranBiovore", "deathCrawler", "necroReanimator" }),
            ("Custodes", "custoBladeChampion", new[] { "custoBladeChampion", "custoTrajann", "worldKharn" }, new[] { "spaceBlackmane", "bloodDante", "bloodMephiston", "blackAbaddon", "custoAtlacoya", "templHelbrecht", "custoVexilusPraetor" }, new[] { "deathCrawler", "tyranBiovore" }),
            ("Laviscus", "emperExultant", new[] { "emperExultant" }, new[] { "custoBladeChampion", "custoTrajann", "worldKharn", "orksWarboss", "worldExecutions", "templHelbrecht", "custoAtlacoya", "custoVexilusPraetor", "admecDominus", "adeptCanoness" }, new[] { "tyranBiovore", "deathCrawler" }),
            ("Multi-Hit", "spaceBlackmane", new[] { "spaceBlackmane", "tauAunShi", "eldarFarseer" }, new[] { "ultraCalgar", "templHelbrecht", "ultraInceptorSgt", "darkaCompanion", "darkaAsmodai", "eldarMauganRa", "worldKharn" }, new[] { "tyranBiovore", "deathCrawler" }),
            ("Neuro", "tyranNeurothrope", new[] { "tyranNeurothrope" }, new[] { "thousInfernalMaster", "thousTzaangor", "blackPossession", "thousAhriman", "genesMagus", "adeptCanoness", "templHelbrecht", "custoAtlacoya", "bloodMephiston" }, new[] { "adeptExorcist" }),
            ("Z'Kar", "thousDaemonPrince", new[] { "tyranNeurothrope" }, new[] { "thousAhriman", "thousInfernalMaster", "thousTzaangor", "blackPossession", "blackAbaddon", "bloodMephiston", "thousSorcerer" }, new[] { "thousDaemonPrince" }),
        };

        foreach (var expected in expectedComps)
        {
            var comp = meta.Comps.Single(comp => comp.Id == expected.Item1);
            Assert.Equal(expected.Item2, comp.SignatureUnitId);
            Assert.Equal(expected.Item3, comp.CoreCharacterIds);
            Assert.Equal(expected.Item4, comp.FlexCharacterIds);
            Assert.Equal(expected.Item5, comp.MowIds);
        }

        var avatar = meta.Bosses.Single(boss => boss.BossUnitSetId == "GuildBoss8Boss1EldarAvatar");
        Assert.Equal(["meta", "alternate"], avatar.Recommendations.Select(recommendation => recommendation.Kind));
        Assert.Equal(
            ["emperExultant", "custoBladeChampion", "custoTrajann", "orksWarboss", "worldKharn"],
            avatar.Recommendations[0].HeroIds);
        Assert.Equal("tyranBiovore", avatar.Recommendations[0].MowId);
        Assert.Equal(new GameCatalogGuildRaidMetaEvidenceView(20, 1660000, 2000000), avatar.Recommendations[0].Evidence);
    }

    [Fact]
    public void RaidBossEncountersResolveTheirUnitProgressionIndexAndInlinedModifiers()
    {
        var snapshot = GameCatalogLoader.Load();
        var view = snapshot.RaidBossesView;

        var bossPrimeIds = new HashSet<string>(
            view.Bosses.Concat(view.Primes).Select(unit => unit.UnitSetId), StringComparer.Ordinal);

        var encounters = view.Seasons.Values
            .SelectMany(season => season.Tiers)
            .SelectMany(tier => tier.Sets)
            .SelectMany(set => set.Encounters)
            .ToArray();

        Assert.NotEmpty(encounters);
        Assert.All(encounters, encounter =>
        {
            Assert.False(encounter.UnitSetId.Contains(':'));
            Assert.True(encounter.ProgressionIndex >= 1);
            Assert.True(encounter.EncounterType is "Boss" or "Crystal");
            Assert.All(encounter.FieldNpcIds, npcId => Assert.False(npcId.Contains(':')));
            Assert.All(encounter.Modifiers, modifier =>
            {
                Assert.False(string.IsNullOrWhiteSpace(modifier.ModifierId));
                Assert.False(string.IsNullOrWhiteSpace(modifier.Type));
                Assert.False(string.IsNullOrWhiteSpace(modifier.Target));
            });
        });

        // At least one encounter references a served boss/prime and carries an inlined modifier definition.
        Assert.Contains(encounters, encounter => bossPrimeIds.Contains(encounter.UnitSetId) && encounter.Modifiers.Count > 0);
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
