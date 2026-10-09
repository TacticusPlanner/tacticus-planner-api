using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

public sealed class LegendaryEventCatalogValidatorTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private readonly LegendaryEventCatalogValidator validator =
        new(factory.Services.GetRequiredService<IGameCatalogProvider>());

    private GameCatalogLreView Lre => validator.FindEvent("astarLysander")!;

    [Fact]
    public void FindsCurrentEventsOnly()
    {
        Assert.NotNull(validator.FindEvent("astarLysander"));
        Assert.Null(validator.FindEvent("notAnEvent"));
    }

    [Fact]
    public void AValidTeamHasNoFailures()
    {
        var units = Lre.Alpha.AvailableUnitIds;
        var fields = new LegendaryEventTeamFields(
            "Melee", [units[0], units[1]], units[2], [Lre.Alpha.UnitsRestrictions[0].Index], 3,
            Lre.Alpha.BattleIds.Count, "estimate");

        Assert.Empty(LegendaryEventCatalogValidator.ValidateTeam(Lre, "alpha", fields));
    }

    [Fact]
    public void AnUnknownLaneStopsValidationAtTheLane()
    {
        var failure = Assert.Single(LegendaryEventCatalogValidator.ValidateTeam(
            Lre, "delta", new LegendaryEventTeamFields(null, null, null, null, 9, 99, null)));

        Assert.Equal("laneId", failure.Field);
    }

    [Fact]
    public void ReportsEveryFailingFieldOnce()
    {
        var notAllowed = Lre.Beta.AvailableUnitIds.Except(Lre.Alpha.AvailableUnitIds).First();
        var fields = new LegendaryEventTeamFields(
            "", [notAllowed], notAllowed, [999], 0, 0, "manual");

        var failures = LegendaryEventCatalogValidator.ValidateTeam(Lre, "alpha", fields);

        Assert.Equal(
            ["name", "memberUnitIds", "reserveUnitId", "objectiveIndexes", "run", "expectedBattleClears"],
            failures.Select(failure => failure.Field));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(4, "manual", null)]
    [InlineData(4, null, "expectedBattleClearsSource")]
    [InlineData(null, "estimate", "expectedBattleClearsSource")]
    [InlineData(4, "Manual", "expectedBattleClearsSource")]
    public void DepthAndSourceTravelTogether(int? depth, string? source, string? failingField)
    {
        var fields = new LegendaryEventTeamFields(
            "t", [Lre.Alpha.AvailableUnitIds[0]], null, [], 1, depth, source);

        var failures = LegendaryEventCatalogValidator.ValidateTeam(Lre, "alpha", fields);

        Assert.Equal(failingField is null ? [] : [failingField], failures.Select(failure => failure.Field));
    }

    [Fact]
    public void NotesAreLimitedTo2000Characters()
    {
        Assert.Empty(LegendaryEventCatalogValidator.ValidatePlan(new string('n', 2000)));
        Assert.Equal("notes", Assert.Single(LegendaryEventCatalogValidator.ValidatePlan(new string('n', 2001))).Field);
    }
}
