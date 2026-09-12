using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

public sealed class GuildRaidStatusProjectorTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly GameCatalogSnapshot catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

    [Fact]
    public void NoActiveSeasonSerializesDiscriminatorFreshnessAndNullSeason()
    {
        var response = GuildRaidStatusProjector.Project(
            State(GuildRaidObservationState.NoActiveSeason),
            null,
            catalog,
            ObservedAt.AddMinutes(-1),
            GuildRaidFreshness.Stale);

        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
        Assert.Contains("\"state\":\"noActiveSeason\"", json);
        Assert.Contains("\"freshness\":\"stale\"", json);
        Assert.Contains("\"season\":null", json);
    }

    [Fact]
    public void NoAttacksSelectsFirstConfiguredBossAtCatalogHp()
    {
        var (configId, config) = FirstConfig();
        var response = Project(Season(configId), catalog);

        Assert.Equal(0, response.Season!.TierIndex);
        Assert.Equal(0, response.Season.SetIndex);
        Assert.True(response.Season.Boss.IsUpcoming);
        Assert.Equal(
            config.Tiers[0].Sets[0].Encounters[0].UnitSetId,
            response.Season.Boss.UnitSetId);
        Assert.Equal(response.Season.Boss.MaximumHp, response.Season.Boss.RemainingHp);
    }

    [Fact]
    public void LivingBossUsesObservedHpAndDefeatedBossAdvances()
    {
        var (configId, config) = FirstConfig();
        var first = config.Tiers[0].Sets[0];
        var boss = first.Encounters.First(encounter => encounter.EncounterType == "Boss");
        var season = Season(configId);
        season.Attacks.Add(Attack(first, config.Tiers[0], boss, 123, 456, ObservedAt));

        var living = Project(season, catalog);
        Assert.False(living.Season!.Boss.IsUpcoming);
        Assert.Equal(123, living.Season.Boss.RemainingHp);
        Assert.Equal(456, living.Season.Boss.MaximumHp);

        season.Attacks.Single().RemainingHp = 0;
        var defeated = Project(season, catalog);
        Assert.True(defeated.Season!.Boss.IsUpcoming);
        Assert.Equal(1, defeated.Season.SetIndex);
        Assert.NotEqual(boss.UnitSetId, defeated.Season.Boss.UnitSetId);
    }

    [Fact]
    public void DefeatingLastSetAdvancesTierAndDefeatingLastPositionUsesConfiguredLoop()
    {
        var (configId, config) = FirstConfig();
        var season = Season(configId);
        var firstTier = config.Tiers[0];
        var lastSet = firstTier.Sets[^1];
        season.Attacks.Add(Attack(lastSet, firstTier, Boss(lastSet), 0, 100, ObservedAt));
        var advanced = Project(season, catalog);
        Assert.Equal(1, advanced.Season!.TierIndex);
        Assert.Equal(0, advanced.Season.SetIndex);

        season.Attacks.Clear();
        var lastTier = config.Tiers[^1];
        var finalSet = lastTier.Sets[^1];
        season.Attacks.Add(Attack(finalSet, lastTier, Boss(finalSet), 0, 100, ObservedAt));
        var looped = Project(season, catalog);
        Assert.Equal(4, looped.Season!.TierIndex);
        Assert.Equal(3, looped.Season.SetIndex);
    }

    [Fact]
    public void DefeatingFinalPositionWithAnInvalidConfiguredLoopTargetThrows()
    {
        var (configId, config) = FirstConfig();
        var season = Season(configId);
        var lastTier = config.Tiers[^1];
        var finalSet = lastTier.Sets[^1];
        season.Attacks.Add(Attack(finalSet, lastTier, Boss(finalSet), 0, 100, ObservedAt));

        var invalidCatalog = catalog with
        {
            RaidBossRawData = catalog.RaidBossRawData with
            {
                Seasons = catalog.RaidBossRawData.Seasons.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Key == configId
                        ? pair.Value with { LoopFromTier = -1, LoopFromSet = -1 }
                        : pair.Value),
            },
        };

        Assert.Throws<InvalidOperationException>(() => Project(season, invalidCatalog));
    }

    [Fact]
    public void PrimeHpAndModifiersUseCurrentObservationAndIgnoreOlderLoopAttack()
    {
        var (configId, config) = FirstConfig();
        var tier = config.Tiers[0];
        var set = tier.Sets[0];
        var boss = Boss(set);
        var prime = set.Encounters.First(encounter => encounter.EncounterType != "Boss");
        var season = Season(configId);
        season.Attacks.Add(Attack(set, tier, prime, 1, 1000, ObservedAt.AddMinutes(-3)));
        season.Attacks.Add(Attack(set, tier, boss, 0, 1000, ObservedAt.AddMinutes(-2)));
        var nextSet = tier.Sets[1];
        season.Attacks.Add(Attack(nextSet, tier, Boss(nextSet), 500, 1000, ObservedAt.AddMinutes(-1)));
        season.Attacks.Add(Attack(
            nextSet,
            tier,
            nextSet.Encounters.First(encounter => encounter.EncounterType != "Boss"),
            800,
            1000,
            ObservedAt));

        var response = Project(season, catalog);
        Assert.Equal(1, response.Season!.SetIndex);
        Assert.All(response.Season.Primes, current =>
            Assert.NotEqual(1, current.RemainingHp));

        var currentPrime = response.Season.Primes[0];
        Assert.Equal(1000, currentPrime.MaximumHp);
        Assert.Equal(800, currentPrime.RemainingHp);
        Assert.Contains(currentPrime.Modifiers, modifier => modifier.Active == true);
        Assert.Contains(currentPrime.Modifiers, modifier => modifier.Active == false);
        Assert.All(currentPrime.Modifiers, modifier => Assert.NotNull(modifier.ActivationRemainingHp));
    }

    [Fact]
    public void PrimeWithUnknownCatalogHpKeepsHpThresholdAndActivationNull()
    {
        var (configId, config) = FirstConfig();
        var withoutPrimes = catalog with
        {
            RaidBossesView = catalog.RaidBossesView with { Primes = [] },
        };

        var response = Project(Season(configId), withoutPrimes);
        var prime = Assert.Single(response.Season!.Primes.Take(1));
        Assert.Null(prime.MaximumHp);
        Assert.Null(prime.RemainingHp);
        Assert.All(prime.Modifiers, modifier =>
        {
            Assert.Null(modifier.ActivationRemainingHp);
            Assert.Null(modifier.Active);
        });
    }

    [Fact]
    public void ExplicitMatchingOccurrenceSuppliesEndsAtAndAbsenceDoesNotInferOne()
    {
        var (configId, _) = FirstConfig();
        var season = Season(configId);
        var withoutOccurrences = catalog with { EventOccurrences = [] };
        Assert.Null(Project(season, withoutOccurrences).Season!.EndsAt);

        var occurrence = new GameCatalogEventOccurrence(
            "guild-raid-test",
            "guild-raid-season",
            ObservedAt.AddDays(-1),
            ObservedAt.AddDays(1),
            new Dictionary<string, JsonElement>
            {
                ["season"] = JsonSerializer.SerializeToElement(season.SeasonNumber),
            });
        var withOccurrence = catalog with { EventOccurrences = [.. catalog.EventOccurrences, occurrence] };
        Assert.Equal(occurrence.EndUtc, Project(season, withOccurrence).Season!.EndsAt);
    }

    private (string Id, GameCatalogRaidBossSeasonView Config) FirstConfig()
    {
        var pair = catalog.RaidBossesView.Seasons.OrderBy(item => item.Key, StringComparer.Ordinal).First();
        return (pair.Key, pair.Value);
    }

    private static GuildRaidStatusResponse Project(GuildRaidSeason season, GameCatalogSnapshot source) =>
        GuildRaidStatusProjector.Project(
            State(GuildRaidObservationState.Active, season.Id),
            season,
            source,
            ObservedAt.AddMinutes(-1),
            GuildRaidFreshness.Fresh);

    private static GuildRaidSyncState State(
        GuildRaidObservationState state,
        GuildRaidSeasonId? seasonId = null) => new()
        {
            GuildId = GuildId.From(Guid.NewGuid()),
            State = state,
            ObservedAt = ObservedAt,
            ActiveSeasonId = seasonId,
        };

    private static GuildRaidSeason Season(string configId) => new()
    {
        Id = GuildRaidSeasonId.From(Guid.NewGuid()),
        GuildId = GuildId.From(Guid.NewGuid()),
        SeasonNumber = 77,
        SeasonConfigId = configId,
        ObservedAt = ObservedAt,
    };

    private static GameCatalogRaidBossEncounterView Boss(GameCatalogRaidBossSetView set) =>
        set.Encounters.First(encounter => encounter.EncounterType == "Boss");

    private static GuildRaidAttack Attack(
        GameCatalogRaidBossSetView set,
        GameCatalogRaidBossTierView tier,
        GameCatalogRaidBossEncounterView encounter,
        int remainingHp,
        int maximumHp,
        DateTimeOffset completedAt) => new()
        {
            Id = GuildRaidAttackId.From(Guid.NewGuid()),
            GuildRaidSeasonId = GuildRaidSeasonId.From(Guid.NewGuid()),
            ContentHash = Guid.NewGuid().ToString("N"),
            TacticusUserIdHash = TacticusUserIdHash.From(new byte[32]),
            Tier = tier.Tier,
            Set = set.Set,
            EncounterIndex = encounter.EncounterIndex,
            RemainingHp = remainingHp,
            MaximumHp = maximumHp,
            EncounterType = encounter.EncounterType == "Boss"
                ? GuildRaidEncounterType.Boss
                : GuildRaidEncounterType.SideBoss,
            UnitSetId = encounter.UnitSetId,
            ProgressionIndex = encounter.ProgressionIndex,
            Difficulty = GuildRaidDifficulty.Common,
            DamageType = GuildRaidDamageType.Battle,
            CompletedAt = completedAt,
        };
}
