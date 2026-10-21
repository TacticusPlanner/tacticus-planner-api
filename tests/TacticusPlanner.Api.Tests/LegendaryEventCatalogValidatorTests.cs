using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

public sealed class LegendaryEventCatalogValidatorTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
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
            "Melee", [units[0], units[1], units[2]], units[3], [Lre.Alpha.UnitsRestrictions[0].Index], 3,
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
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, false, true)]
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    public void ATeamHasThreeToFiveMembersAndTheReserveDoesNotCount(int memberCount, bool withReserve, bool valid)
    {
        var units = Lre.Alpha.AvailableUnitIds;
        var fields = new LegendaryEventTeamFields(
            "Team", [.. units.Take(memberCount)], withReserve ? units[memberCount] : null, [], 1, null, null);

        var failures = LegendaryEventCatalogValidator.ValidateTeam(Lre, "alpha", fields);

        if (valid)
        {
            Assert.Empty(failures);
        }
        else
        {
            var failure = Assert.Single(failures);
            Assert.Equal("memberUnitIds", failure.Field);
            Assert.Equal("A team has 3–5 members.", failure.Message);
        }
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
            "t", [.. Lre.Alpha.AvailableUnitIds.Take(3)], null, [], 1, depth, source);

        var failures = LegendaryEventCatalogValidator.ValidateTeam(Lre, "alpha", fields);

        Assert.Equal(failingField is null ? [] : [failingField], failures.Select(failure => failure.Field));
    }

    [Fact]
    public void NotesAreLimitedTo2000Characters()
    {
        Assert.Empty(LegendaryEventCatalogValidator.ValidatePlan(new string('n', 2000)));
        Assert.Equal("notes", Assert.Single(LegendaryEventCatalogValidator.ValidatePlan(new string('n', 2001))).Field);
    }

    [Theory]
    [InlineData("1", 10, 10, 75, null)]
    [InlineData("3", 0, 0, 0, null)]
    [InlineData("4", 0, 0, 0, "run")]
    [InlineData("01x", 0, 0, 0, "run")]
    [InlineData(null, 0, 0, 0, "run")]
    [InlineData("2", 11, 0, 0, "regularMissions")]
    [InlineData("2", 0, -1, 0, "premiumMissions")]
    [InlineData("2", 0, 0, 76, "closeShards")]
    public void RunInputsAreBoundedByTheEventsMissions(string? run, int regular, int premium, int closeShards, string? failingField)
    {
        var failures = LegendaryEventCatalogValidator.ValidateRunInputs(
            Lre, run, new UpdateLegendaryEventRunInputsRequest(0, regular, premium, true, closeShards));

        Assert.Equal(failingField is null ? [] : [failingField], failures.Select(failure => failure.Field));
    }

    [Fact]
    public void AnnotationsAreBoundedByTheLane()
    {
        var battles = Lre.Alpha.BattleIds.Count;
        var objectives = Lre.Alpha.UnitsRestrictions.Count;

        Assert.Empty(LegendaryEventCatalogValidator.ValidateAnnotations(
            Lre, new UpdateLegendaryEventAnnotationsRequest(0, "alpha", battles - 1, [0, objectives], null)));
        Assert.Equal(["battleIndex", "objectiveIds", "status"], LegendaryEventCatalogValidator.ValidateAnnotations(
                Lre, new UpdateLegendaryEventAnnotationsRequest(0, "alpha", battles, [objectives + 1], "blocked"))
            .Select(failure => failure.Field));
        Assert.Equal("laneId", Assert.Single(LegendaryEventCatalogValidator.ValidateAnnotations(
            Lre, new UpdateLegendaryEventAnnotationsRequest(0, "delta", 0, [0], "stopHere"))).Field);
    }
}
