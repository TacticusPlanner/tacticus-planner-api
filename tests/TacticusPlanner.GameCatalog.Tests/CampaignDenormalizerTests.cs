using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class CampaignDenormalizerTests
{
    private static GameCatalogCampaignBattle Battle(
        string id,
        int battleIndex,
        bool challenge = false,
        int nodeNumber = 1,
        string type = "Standard") =>
        new(id, type, challenge, 6, nodeNumber, 5, new GameCatalogCampaignRewards([], []), 0, [], [], 0, [], [], [])
        {
            BattleIndex = battleIndex,
        };

    [Fact]
    public void ServedBattlesCarryTheirCatalogBattleIndexUnchanged()
    {
        // A regular node and its challenge variant share nodeNumber 3 but must carry distinct,
        // already-assigned battleIndex values through to the served view (see GameCatalogCampaignBattle.BattleIndex).
        var regular = Battle("AMS3", battleIndex: 2, nodeNumber: 3);
        var challengeBattle = Battle("AMSC3B", battleIndex: 3, challenge: true, nodeNumber: 3);
        var group = new GameCatalogCampaignGroup("eventCampaign1", "AdeptusMechanicus", "event", "Chaos", ["DeathGuard"], [], ["Standard"],
            [regular, challengeBattle]);

        var views = GameCatalogDenormalizer.BuildCampaignBattles(
            new Dictionary<string, GameCatalogCampaignGroup> { [group.GroupId] = group }, []);

        Assert.Equal(2, views.Single(view => view.Id == "AMS3").BattleIndex);
        Assert.Equal(3, views.Single(view => view.Id == "AMSC3B").BattleIndex);
    }

    [Fact]
    public void EarlyIndomitusNodesCapAtThreeAttemptsAndTheRestKeepTheirTypeCap()
    {
        var group = new GameCatalogCampaignGroup("campaign1", "Ultramarines", "standard", "Imperial", ["Ultramarines"], [], ["Standard", "Elite"],
        [
            Battle("I05", battleIndex: 4, nodeNumber: 5),
            Battle("I06", battleIndex: 5, nodeNumber: 6),
            Battle("I14", battleIndex: 13, nodeNumber: 14),
            Battle("I15", battleIndex: 14, nodeNumber: 15),
            Battle("IE10", battleIndex: 9, nodeNumber: 10, type: "Elite"),
        ]);
        var other = new GameCatalogCampaignGroup("campaign4", "Aeldari", "standard", "Xenos", ["Aeldari"], [], ["Standard"],
            [Battle("SH06", battleIndex: 5, nodeNumber: 6)]);

        var views = GameCatalogDenormalizer.BuildCampaignBattles(
            new Dictionary<string, GameCatalogCampaignGroup> { [group.GroupId] = group, [other.GroupId] = other }, []);

        int Attempts(string id) => views.Single(view => view.Id == id).DailyAttempts;
        Assert.Equal(10, Attempts("I05"));
        Assert.Equal(3, Attempts("I06"));
        Assert.Equal(3, Attempts("I14"));
        Assert.Equal(10, Attempts("I15"));
        Assert.Equal(6, Attempts("IE10"));
        Assert.Equal(10, Attempts("SH06"));
    }
}
