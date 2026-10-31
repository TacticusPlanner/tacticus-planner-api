using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerDataOverrides;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

public sealed class BattleProgressOverrideValidationTests(GameCatalogApiFactory factory)
    : IClassFixture<GameCatalogApiFactory>
{
    private GameCatalogSnapshot Catalog => factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

    private GameCatalogCampaignBattle StandingBattle(bool withLightningVictory) => Catalog.CampaignGroups.Values
        .Where(group => !string.Equals(group.ReleaseType, "event", StringComparison.OrdinalIgnoreCase))
        .SelectMany(group => group.Battles)
        .First(battle => battle.LightningVictoryTurns is not null == withLightningVictory);

    private GameCatalogCampaignBattle EventBattle => Catalog.CampaignGroups.Values
        .Where(group => string.Equals(group.ReleaseType, "event", StringComparison.OrdinalIgnoreCase))
        .SelectMany(group => group.Battles)
        .First(battle => battle.LightningVictoryTurns is not null);

    private bool Validate(IReadOnlyList<BattleProgressOverrideRequest> battles) =>
        UpdateBattleProgressEndpoint.ValidateAgainstCatalog(battles, Catalog, out _);

    [Fact]
    public void AcceptsKnownStandingAndEventBattles()
    {
        Assert.True(Validate([
            new(StandingBattle(withLightningVictory: true).Id, 2, true, null),
            new(EventBattle.Id, 3, true, BattleMilestone.Gold),
        ]));
    }

    [Fact]
    public void RejectsUnknownBattleIds()
    {
        Assert.False(UpdateBattleProgressEndpoint.ValidateAgainstCatalog(
            [new("NotABattle", 2, null, null)], Catalog, out var error));
        Assert.Contains("NotABattle", error);
    }

    [Fact]
    public void RejectsLightningVictoryOnBattlesWithoutOne()
    {
        Assert.False(Validate([new(StandingBattle(withLightningVictory: false).Id, 3, true, null)]));
        Assert.True(Validate([new(StandingBattle(withLightningVictory: false).Id, 3, false, null)]));
    }

    [Fact]
    public void RejectsMilestonesOutsideCampaignEvents()
    {
        Assert.False(Validate([new(StandingBattle(withLightningVictory: true).Id, 3, null, BattleMilestone.Bronze)]));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void ValidatorBoundsMedals(int medals, bool valid)
    {
        var result = new UpdateBattleProgressValidator().Validate(
            new UpdateBattleProgressRequest([new("I06", medals, null, null)], 0));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void ValidatorRejectsDuplicateBattleIds()
    {
        var result = new UpdateBattleProgressValidator().Validate(
            new UpdateBattleProgressRequest([new("I06", 1, null, null), new("I06", 2, null, null)], 0));
        Assert.False(result.IsValid);
    }
}
