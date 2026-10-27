using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class ProgressionRulesDenormalizerTests
{
    private static readonly Lazy<GameCatalogSnapshot> Catalog = new(GameCatalogLoader.Load);

    private static GameCatalogProgressionRules Shipped => Assert.Single(Catalog.Value.ProgressionRulesViews);

    private static readonly string[] Rarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    private static DatamineConsumables Books() =>
        new(Rarities.Select((rarity, index) => new DatamineXpBook($"xp{rarity}", rarity, 20 * (index + 1), 5)).ToArray());

    private static DatamineUpgradeSlots Slots(int rows = 20, int perRow = 6) =>
        new(Enumerable.Range(0, rows).Select(row => (IReadOnlyList<int>)Enumerable.Repeat(row + 1, perRow).ToArray()).ToArray());

    private static (string Rarity, int Stars)[] Ladder() =>
        ProgressionRules.ProgressionOrder.Select(progression =>
        {
            var parts = progression.Split(':');
            var stars = Array.IndexOf(
                ["None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars", "RedOneStar", "RedTwoStars", "RedThreeStars",
                 "RedFourStars", "RedFiveStars", "OneBlueStar", "TwoBlueStars", "ThreeBlueStars", "MythicWings"],
                parts[1]);
            return (parts[0], stars);
        }).ToArray();

    private static List<DatamineHeroProgressionRule> HeroSteps() =>
        Ladder().Select(step => new DatamineHeroProgressionRule(step.Stars, step.Rarity, 8, 3, 100, 100, null)).ToList();

    private static List<DatamineMowProgressionRule> MowSteps() =>
        Ladder().Select(step => new DatamineMowProgressionRule(step.Stars, step.Rarity, 8, 100, null)).ToList();

    private static IReadOnlyList<GameCatalogProgressionRules> Read(
        IReadOnlyList<int>? xpLevels = null,
        DatamineConsumables? books = null,
        DatamineUpgradeSlots? slots = null,
        IReadOnlyList<DatamineHeroProgressionRule>? heroSteps = null,
        IReadOnlyList<DatamineMowProgressionRule>? mowSteps = null) =>
        GameCatalogDenormalizer.ReadDatamineProgressionRules(
            xpLevels ?? [25, 60], books ?? Books(), slots ?? Slots(), heroSteps ?? HeroSteps(), mowSteps ?? MowSteps());

    [Fact]
    public void TheShippedXpCurveAndBooksAreTheGames()
    {
        Assert.Equal(64, Shipped.XpLevels.Count);
        Assert.Equal([25, 60, 120], Shipped.XpLevels.Take(3));
        Assert.Equal(7_360_200, Shipped.XpLevels[^1]);
        Assert.Equal(Rarities, Shipped.XpBooks.Select(book => book.Rarity));
        Assert.Equal(new GameCatalogXpBook("xpLegendary", "Legendary", 12_500, 500), Shipped.XpBooks[4]);
    }

    [Fact]
    public void TheShippedUpgradeSlotsAreKeyedByRank()
    {
        Assert.Equal(20, Shipped.UpgradeSlots.Count);
        Assert.Equal(new GameCatalogUpgradeSlotRank("Stone1", [1, 1, 1, 1, 2, 3]), Shipped.UpgradeSlots[0], new SlotComparer());
        Assert.Equal("Adamantine2", Shipped.UpgradeSlots[^1].Rank);
        Assert.Equal([47, 48, 47, 49, 47, 50], Shipped.UpgradeSlots.Single(row => row.Rank == "Diamond2").XpLevelRequirements);
    }

    [Fact]
    public void TheShippedLaddersWalkTheProgressionOrder()
    {
        Assert.Equal(ProgressionRules.ProgressionOrder, Shipped.HeroProgressionSteps.Select(step => step.Progression));
        Assert.Equal(ProgressionRules.ProgressionOrder, Shipped.MowProgressionSteps.Select(step => step.Progression));
        Assert.Equal(
            new GameCatalogHeroProgressionRule(0, "Common:None", "Common", 0, 8, "Iron1", 100, 100, 25),
            Shipped.HeroProgressionSteps[0]);
        Assert.Equal(
            new GameCatalogHeroProgressionRule(19, "Mythic:MythicWings", "Mythic", 14, 60, "Adamantine3", 240, 200, null),
            Shipped.HeroProgressionSteps[^1]);
        Assert.Equal("Diamond3", Shipped.HeroProgressionSteps.Single(step => step.Progression == "Legendary:RedThreeStars").MaxRank);
        Assert.Equal(
            new GameCatalogMowProgressionRule(19, "Mythic:MythicWings", "Mythic", 14, 60, 200, 200),
            Shipped.MowProgressionSteps[^1]);
        Assert.Null(Shipped.MowProgressionSteps[15].MythicAbilityStatMultiplierPct);
    }

    [Fact]
    public void BooksAreServedInRarityOrderWhateverTheFileOrder()
    {
        var rules = Read(books: new DatamineConsumables(Books().XpBooks!.Reverse().ToArray()));

        Assert.Equal(Rarities, Assert.Single(rules).XpBooks.Select(book => book.Rarity));
    }

    public static TheoryData<string, Func<IReadOnlyList<GameCatalogProgressionRules>>> Invalid() => new()
    {
        { "xpLevels must be positive and strictly increasing", () => Read(xpLevels: [25, 25]) },
        { "xpLevels must be positive", () => Read(xpLevels: []) },
        { "one book per rarity", () => Read(books: new DatamineConsumables(Books().XpBooks!.Skip(1).ToArray())) },
        { "has no xpBooks", () => Read(books: new DatamineConsumables(null)) },
        { "must have 20 rows", () => Read(slots: Slots(rows: 19)) },
        { "must have 20 rows", () => Read(slots: Slots(perRow: 5)) },
        { "heroProgressionSteps row 0 has maxRank 21", () => Read(heroSteps: [HeroSteps()[0] with { MaxRank = 21 }, .. HeroSteps().Skip(1)]) },
        { "heroProgressionSteps must walk exactly", () => Read(heroSteps: HeroSteps().Skip(1).ToArray()) },
        { "heroProgressionStepsMoW row 1 has rarity 'Relic'", () => Read(mowSteps: [MowSteps()[0], MowSteps()[1] with { Rarity = "Relic" }, .. MowSteps().Skip(2)]) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AMisshapenTableFailsTheLoad(string expected, Func<IReadOnlyList<GameCatalogProgressionRules>> read)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => read());

        Assert.Contains("'progression-rules'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private sealed class SlotComparer : IEqualityComparer<GameCatalogUpgradeSlotRank>
    {
        public bool Equals(GameCatalogUpgradeSlotRank? x, GameCatalogUpgradeSlotRank? y) =>
            x is not null && y is not null && x.Rank == y.Rank && x.XpLevelRequirements.SequenceEqual(y.XpLevelRequirements);

        public int GetHashCode(GameCatalogUpgradeSlotRank obj) => obj.Rank.GetHashCode(StringComparison.Ordinal);
    }
}
