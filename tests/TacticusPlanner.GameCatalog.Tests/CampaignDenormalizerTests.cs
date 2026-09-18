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
        var group = new GameCatalogCampaignGroup("eventCampaign1", "AdeptusMechanicus", "event", [], ["Standard"],
            [regular, challengeBattle]);

        var views = GameCatalogDenormalizer.BuildCampaignBattles(
            new Dictionary<string, GameCatalogCampaignGroup> { [group.GroupId] = group }, []);

        Assert.Equal(2, views.Single(view => view.Id == "AMS3").BattleIndex);
        Assert.Equal(3, views.Single(view => view.Id == "AMSC3B").BattleIndex);
    }
}
