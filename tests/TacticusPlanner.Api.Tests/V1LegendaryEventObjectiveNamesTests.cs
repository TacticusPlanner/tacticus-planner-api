using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

/// <summary>The regenerated V1 objective display names (design D16 of add-legendary-event-teams): each
/// lane's objectives stay distinguishable after normalisation across the whole embedded catalog, and the
/// documented V1 spellings resolve.</summary>
public sealed class V1LegendaryEventObjectiveNamesTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public void RegeneratedNamesAreUniquePerLaneAcrossTheCatalog()
    {
        var catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

        Assert.NotEmpty(catalog.LreViews);
        foreach (var lre in catalog.LreViews)
        {
            foreach (var (_, lane) in lre.Lanes())
            {
                var names = lane.UnitsRestrictions
                    .Select(objective => V1LegendaryEventObjectiveNames.Normalize(V1LegendaryEventObjectiveNames.V1DisplayName(objective.Filter)))
                    .ToList();
                Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
                foreach (var objective in lane.UnitsRestrictions)
                {
                    Assert.Same(objective, V1LegendaryEventObjectiveNames.Resolve(lane, objective.Name));
                    Assert.Same(objective, V1LegendaryEventObjectiveNames.Resolve(lane, V1LegendaryEventObjectiveNames.V1DisplayName(objective.Filter)));
                }
            }
        }
    }

    [Theory]
    [InlineData("Trait", "Resilient", true, "No Resilient")]
    [InlineData("Trait", "TerminatorArmour", true, "No Terminator")]
    [InlineData("Trait", "TerminatorArmour", false, "Terminator")]
    [InlineData("Trait", "TeleportStrike", false, "Deep Strike")]
    [InlineData("Trait", "RapidAssault", false, "Rapid Assault")]
    [InlineData("Faction", "Genestealers", false, "Genestealer Cults")]
    [InlineData("Faction", "BlackTemplars", false, "Black Templars")]
    [InlineData("DamageType", "Bolter", true, "No Bolter")]
    [InlineData("DamageType", "HeavyRound", false, "Heavy Round")]
    [InlineData("MinHits", "5", false, "Min 5 hits")]
    [InlineData("MaxHits", "2", false, "Max 2 hits")]
    [InlineData("AttackType", "Ranged", true, "Melee")]
    [InlineData("AttackType", "Ranged", false, "Ranged")]
    [InlineData("NoSummons", "", false, "No Summons")]
    public void V1DisplayNamesMatchTheV1Convention(string kind, string target, bool exclude, string expected)
    {
        Assert.Equal(expected, V1LegendaryEventObjectiveNames.V1DisplayName(new GameCatalogLreFilter(kind, target, exclude)));
    }

    [Theory]
    [InlineData("No Resiliant", "no resilient")]
    [InlineData("  min   5 HITS ", "min 5 hits")]
    [InlineData("No Range", "no ranged")]
    public void NormalisationLowersCollapsesAndAliases(string input, string expected)
    {
        Assert.Equal(expected, V1LegendaryEventObjectiveNames.Normalize(input));
    }

    [Fact]
    public void UnknownOrBlankNamesDoNotResolve()
    {
        var catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;
        var lane = catalog.FindLre("astarLysander")!.Alpha;

        Assert.Null(V1LegendaryEventObjectiveNames.Resolve(lane, "Teleport"));
        Assert.Null(V1LegendaryEventObjectiveNames.Resolve(lane, "  "));
        Assert.Null(V1LegendaryEventObjectiveNames.Resolve(lane, null));
    }
}
