using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Covers the served plan's mission ticks against the current catalog: ticks past an event's mission lists (a
/// refresh shortened them) are dropped on read, so the client never resends an index a run write would reject.
/// </summary>
public sealed class LegendaryEventPlanProjectionTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private readonly GameCatalogLreView uthar = factory.Services.GetRequiredService<IGameCatalogProvider>()
        .Current.LreViews.First(view => view.Id == "votanUthar");

    private static LegendaryEventPlan Plan(List<int> regular, List<int> premium) => new()
    {
        EventId = "votanUthar",
        CatalogVersion = "1",
        Runs = [new LegendaryEventRunProgress
        {
            Run = 1,
            RegularMissions = 10,
            PremiumMissions = 10,
            CompletedRegularMissions = regular,
            CompletedPremiumMissions = premium,
        }],
    };

    [Fact]
    public void TicksPastTheCatalogMissionListsAreDropped()
    {
        var shortened = uthar with
        {
            RegularMissions = uthar.RegularMissions.Take(9).ToList(),
            PremiumMissions = uthar.PremiumMissions.Take(5).ToList(),
        };

        var run = Assert.Single(LegendaryEventPlanProjection.Map(Plan([9, 2, 8], [7, 0]), shortened).Runs);

        Assert.Equal([2, 8], run.CompletedRegularMissions);
        Assert.Equal([0], run.CompletedPremiumMissions);
    }

    [Fact]
    public void TicksAreServedSortedWhenTheCatalogEventIsGone()
    {
        var run = Assert.Single(LegendaryEventPlanProjection.Map(Plan([9, 2], [7, 0])).Runs);

        Assert.Equal([2, 9], run.CompletedRegularMissions);
        Assert.Equal([0, 7], run.CompletedPremiumMissions);
    }
}
