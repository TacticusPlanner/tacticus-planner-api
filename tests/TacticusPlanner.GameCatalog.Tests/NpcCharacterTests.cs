using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class NpcCharacterTests
{
    private static GameCatalogRawNpc Npc(string id, string name) =>
        new(id, name, "Physical", 1, null, null, null, 3, [], [], [], [], [], []);

    private static GameCatalogCharacter Character(string id, string name) =>
        new(id, name, 100, 10, 5, "Common", "Physical", 1, null, null, null, 3, [], [], [], [], [], [], []);

    private static readonly IReadOnlyDictionary<string, GameCatalogFactionNpcs> Npcs =
        new Dictionary<string, GameCatalogFactionNpcs>(StringComparer.Ordinal)
        {
            ["npcs-aeldari"] = new("Xenos", "Aeldari", "Aeldari",
            [
                Npc("eldarBossMauganRaUltra", "Harvester of Souls"),
                Npc("eldarNpcMauganRa", "Maugan Ra"),
                Npc("eldarNpc1Guardian", "Guardian"),
            ]),
            ["npcs-necrons"] = new("Xenos", "Necrons", "Necrons",
            [
                Npc("necroNpcWarden", "Makhotep"),
                // Same name as an Aeldari character, other faction: not that character.
                Npc("necroNpcLookalike", "Maugan Ra"),
            ]),
        };

    private static readonly IReadOnlyDictionary<string, GameCatalogFactionUnits> Units =
        new Dictionary<string, GameCatalogFactionUnits>(StringComparer.Ordinal)
        {
            ["units-aeldari"] = new("Xenos", "Aeldari", "Aeldari", [Character("eldarMauganRa", "Maugan Ra")], []),
            ["units-necrons"] = new("Xenos", "Necrons", "Necrons", [Character("necroWarden", "Makhotep")], []),
        };

    private static IReadOnlyDictionary<string, string> Read(Dictionary<string, string?> curated) =>
        GameCatalogDenormalizer.ReadNpcCharacters(curated, Npcs, Units);

    [Fact]
    public void LinksAnNpcToTheSameNamedCharacterOfItsFaction()
    {
        var links = Read([]);

        Assert.Equal("eldarMauganRa", links["eldarNpcMauganRa"]);
        Assert.Equal("necroWarden", links["necroNpcWarden"]);
        Assert.False(links.ContainsKey("eldarNpc1Guardian"));
        Assert.False(links.ContainsKey("necroNpcLookalike"));
        Assert.False(links.ContainsKey("eldarBossMauganRaUltra"));
    }

    [Fact]
    public void CuratedEntriesLinkADifferentNameAndUnlinkAFalseMatch()
    {
        var links = Read(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["eldarBossMauganRaUltra"] = "eldarMauganRa",
            ["necroNpcWarden"] = null,
        });

        Assert.Equal("eldarMauganRa", links["eldarBossMauganRaUltra"]);
        Assert.False(links.ContainsKey("necroNpcWarden"));
    }

    [Theory]
    [InlineData("eldarNpcMissing", "eldarMauganRa", "'eldarNpcMissing' is not an NPC")]
    [InlineData("eldarBossMauganRaUltra", "eldarAvatar", "links 'eldarAvatar', which is not a character")]
    [InlineData("eldarNpcMauganRa", "eldarMauganRa", "'eldarNpcMauganRa' repeats what the name match gives")]
    [InlineData("eldarNpc1Guardian", null, "'eldarNpc1Guardian' repeats what the name match gives")]
    public void RejectsABadCuratedEntry(string npcId, string? characterId, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Read(new Dictionary<string, string?>(StringComparer.Ordinal) { [npcId] = characterId }));

        Assert.Contains(GameCatalogDatasets.NpcCharacters, error.Message);
        Assert.Contains(message, error.Message);
    }
}
