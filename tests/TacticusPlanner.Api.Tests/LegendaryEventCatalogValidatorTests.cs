using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog;

namespace TacticusPlanner.Api.Tests;

/// <summary>Direct coverage of the catalog validation shared by create, update and the V1 import
/// (add-legendary-event-teams, design D6): every failure names the offending field, and a valid write
/// yields none. The embedded <c>astarLysander</c> alpha lane excludes Xenos and has 18 battles.</summary>
public sealed class LegendaryEventCatalogValidatorTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string U1 = "ultraInceptorSgt";
    private const string U2 = "astarCyrus";
    private const string XenosUnit = "eldarAutarch";

    private LegendaryEventCatalogValidator Validator => new(factory.Services.GetRequiredService<IGameCatalogProvider>());

    [Fact]
    public void FindEventKnowsCatalogIdsOnly()
    {
        var validator = Validator;

        Assert.NotNull(validator.FindEvent("astarLysander"));
        Assert.Null(validator.FindEvent("ASTARLYSANDER"));
        Assert.Null(validator.FindEvent("nope"));
        Assert.Null(validator.FindEvent(null));
    }

    [Fact]
    public void AValidTeamHasNoFailures()
    {
        var lre = Validator.FindEvent("astarLysander")!;

        var failures = LegendaryEventCatalogValidator.ValidateTeam(
            lre, "alpha", " Melee ", [U1, U2], "admecDominus", [0, 4],
            new LegendaryEventRunDepthWrite(3, 18, LegendaryEventDepthSource.Estimate));

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryRuleNamesItsField()
    {
        var lre = Validator.FindEvent("astarLysander")!;

        var failures = LegendaryEventCatalogValidator.ValidateTeam(
            lre, "alpha", "", [U1, XenosUnit, U1], U1, [5, 0, 0],
            new LegendaryEventRunDepthWrite(4, 19, null));

        Assert.Equal(
            ["expectedBattleClears", "expectedBattleClearsSource", "memberUnitIds", "name", "objectiveIndexes", "reserveUnitId", "run"],
            failures.Select(failure => failure.Field).Distinct().Order());
    }

    [Fact]
    public void UnknownLaneFailsOnLaneIdWithoutLaneChecks()
    {
        var lre = Validator.FindEvent("astarLysander")!;

        var failures = LegendaryEventCatalogValidator.ValidateTeam(lre, "delta", "Team", [XenosUnit], null, [9], null);

        Assert.Equal(["laneId"], failures.Select(failure => failure.Field));
    }

    [Fact]
    public void DepthRulesAreSymmetric()
    {
        var lane = Validator.FindEvent("astarLysander")!.Alpha;

        var sourceOnly = LegendaryEventCatalogValidator.ValidateDepth(lane, new(1, null, LegendaryEventDepthSource.Manual));
        var depthOnly = LegendaryEventCatalogValidator.ValidateDepth(lane, new(1, 5, null));
        var clear = LegendaryEventCatalogValidator.ValidateDepth(lane, new(2, null, null));

        Assert.Equal(["expectedBattleClearsSource"], sourceOnly.Select(failure => failure.Field));
        Assert.Equal(["expectedBattleClearsSource"], depthOnly.Select(failure => failure.Field));
        Assert.Empty(clear);
    }

    [Fact]
    public void NotesAreCappedAtTwoThousandCharacters()
    {
        Assert.Empty(LegendaryEventCatalogValidator.ValidateNotes(new string('n', 2000)));
        Assert.Empty(LegendaryEventCatalogValidator.ValidateNotes(null));
        Assert.Equal(["notes"], LegendaryEventCatalogValidator.ValidateNotes(new string('n', 2001)).Select(failure => failure.Field));
    }
}
