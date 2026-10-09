using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class V1LegendaryEventObjectiveNamesTests
{
    private static readonly GameCatalogSnapshot Catalog = GameCatalogLoader.Load();

    public static TheoryData<string, string, bool, string> V1Names => new()
    {
        { "Trait", "Resilient", false, "Resilient" },
        { "Trait", "Resilient", true, "No Resilient" },
        { "Trait", "TerminatorArmour", false, "Terminator" },
        { "Trait", "TerminatorArmour", true, "No Terminator" },
        { "Trait", "TeleportStrike", false, "Deep Strike" },
        { "Trait", "FinalJustice", false, "Final Vengeance" },
        { "Trait", "SuppressiveFire", false, "Suppressive Fire" },
        { "Faction", "BlackTemplars", false, "Black Templars" },
        { "DamageType", "Bolter", true, "No Bolter" },
        { "MinHits", "5", false, "Min 5 hits" },
        { "MaxHits", "2", false, "Max 2 hits" },
        { "AttackType", "Ranged", false, "Ranged" },
        { "AttackType", "Ranged", true, "Melee" },
    };

    [Theory]
    [MemberData(nameof(V1Names))]
    public void RegeneratesV1DisplayNames(string kind, string target, bool exclude, string expected) =>
        Assert.Equal(expected, V1LegendaryEventObjectiveNames.DisplayName(new GameCatalogLreFilter(kind, target, exclude)));

    [Fact]
    public void EveryCatalogObjectiveMatchesItsV1AndCatalogNamesAndNoLaneHasTwoAlike()
    {
        foreach (var lre in Catalog.LreViews)
        {
            foreach (var lane in new[] { lre.Alpha, lre.Beta, lre.Gamma })
            {
                var regenerated = lane.UnitsRestrictions
                    .Select(restriction => V1LegendaryEventObjectiveNames.Normalize(
                        V1LegendaryEventObjectiveNames.DisplayName(restriction.Filter)))
                    .ToList();
                Assert.Equal(regenerated.Count, regenerated.Distinct().Count());

                foreach (var restriction in lane.UnitsRestrictions)
                {
                    var v1Name = V1LegendaryEventObjectiveNames.DisplayName(restriction.Filter);
                    Assert.Equal([restriction.Index], Resolve(lane, v1Name));
                    Assert.Equal([restriction.Index], Resolve(lane, restriction.Name));
                }
            }
        }
    }

    [Theory]
    [InlineData("No Resiliant", "Trait", "Resilient", true)]
    [InlineData("no   resilient", "Trait", "Resilient", true)]
    [InlineData("No Range", "AttackType", "Ranged", true)]
    [InlineData("MIN 5 HITS", "MinHits", "5", false)]
    public void LegacySpellingsAndCasingMatch(string v1Name, string kind, string target, bool exclude)
    {
        var restriction = new GameCatalogLreRestriction("Catalog name", 10, 0, new GameCatalogLreFilter(kind, target, exclude));

        Assert.True(V1LegendaryEventObjectiveNames.Matches(v1Name, restriction));
    }

    [Fact]
    public void BlankAndForeignNamesDoNotMatch()
    {
        var restriction = new GameCatalogLreRestriction("Flying", 10, 0, new GameCatalogLreFilter("Trait", "Flying", false));

        Assert.False(V1LegendaryEventObjectiveNames.Matches("  ", restriction));
        Assert.False(V1LegendaryEventObjectiveNames.Matches("No Flying", restriction));
    }

    [Fact]
    public void ServedEventsResolveFromTheirV1NumericId()
    {
        foreach (var lre in Catalog.LreViews)
        {
            var raw = Catalog.Lres.Single(entry => entry.UnitSnowprintId == lre.Id);
            Assert.Equal(lre.Id, Catalog.ServedLreIdForV1Id(raw.Id));
        }

        Assert.Null(Catalog.ServedLreIdForV1Id(10));
    }

    private static List<int> Resolve(GameCatalogLreTrackView lane, string v1Name) =>
        lane.UnitsRestrictions
            .Where(restriction => V1LegendaryEventObjectiveNames.Matches(v1Name, restriction))
            .Select(restriction => restriction.Index)
            .ToList();
}
