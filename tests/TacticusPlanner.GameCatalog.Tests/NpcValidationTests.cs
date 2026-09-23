using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class NpcValidationTests
{
    private static GameCatalogRawNpc Npc(string id, params string[] traits) =>
        new(id, id, "Physical", 1, null, null, null, 0, traits, [], [], [], [], [new GameCatalogNpcStat(1, 0, 0, 0, 0, 0, 0)]);

    private static GameCatalogFactionNpcs Faction(string factionId, params GameCatalogRawNpc[] npcs) =>
        new("Neutral", factionId, factionId, npcs);

    private static List<GameCatalogValidationError> Validate(params (string key, GameCatalogFactionNpcs faction)[] files)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateNpcs(files.ToDictionary(file => file.key, file => file.faction, StringComparer.Ordinal), errors);
        return errors;
    }

    [Fact]
    public void RejectsAnObjectThatCarriesTheMachineOfWarTrait()
    {
        var errors = Validate(("npcs-objects", Faction("Objects", Npc("LootObj_Tank", "Object", "MachineOfWar"))));

        var error = Assert.Single(errors);
        Assert.Equal("ConflictingNpcKind", error.Code);
        Assert.Contains("LootObj_Tank", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsMachinesOfWarInFactionFilesAndPlainObjects()
    {
        var errors = Validate(
            ("npcs-objects", Faction("Objects", Npc("LootObj_AmmoBox", "Object"), Npc("LootObj_CoinCrate"))),
            ("npcs-necrons", Faction("Necrons", Npc("necroNpcMoWReanimator", "MachineOfWar"))));

        Assert.Empty(errors);
    }

    [Fact]
    public void TheEmbeddedCatalogClassifiesExactlyTheExpectedKinds()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal(11, snapshot.NpcList.Count(npc => npc.Kind == "machineOfWar"));
        Assert.Equal(56, snapshot.NpcList.Count(npc => npc.Kind == "object"));
        Assert.All(snapshot.NpcList.Where(npc => npc.Kind == "object"), npc => Assert.Equal("Objects", npc.FactionId));
        Assert.Equal(534, snapshot.NpcList.Count);

        var warden = snapshot.NpcList.Single(npc => npc.Id == "necroBossWarden");
        Assert.Equal(("Necrons", "Xenos", "unit"), (warden.FactionId, warden.Alliance, warden.Kind));
    }
}
