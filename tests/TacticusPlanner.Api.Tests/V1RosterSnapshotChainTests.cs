using System.Text.Json;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

/// <summary>The port of V1 <c>RosterSnapshotsService.resolveSnapshotDiff</c> and <c>fixSnapshot</c>.</summary>
public sealed class V1RosterSnapshotChainTests
{
    private static V1RosterSnapshotsState Read(string json)
    {
        var data = TacticusV1Client.ReadRosterSnapshots(JsonDocument.Parse(json).RootElement);
        return Assert.IsType<V1RosterSnapshotsState>(data.State);
    }

    [Fact]
    public void ABaseOnlyChainResolvesToTheBase()
    {
        var state = Read("""
            {"base": {"name": "Only", "dateMillisUtc": 1000, "chars": [
              {"id": "a", "rank": 5, "rarity": 1, "stars": 3, "shards": 1, "mythicShards": 0,
               "activeAbilityLevel": 2, "passiveAbilityLevel": 3, "xpLevel": 4,
               "equip0": {"id": "item1", "type": "I_Crit"}, "equip0Level": 2}
            ], "mows": []}, "diffs": []}
            """);

        var snapshot = Assert.Single(V1RosterSnapshotChain.Resolve(state));

        Assert.Equal(("Only", 1000L, false), (snapshot.Name, snapshot.DateMillisUtc, snapshot.Deleted));
        var unit = Assert.Single(snapshot.Characters);
        Assert.Equal(("item1", (int?)2), unit.Equipment[0]);
        Assert.Equal((null, null), unit.Equipment[1]);
    }

    [Fact]
    public void EachDiffAppliesToThePreviousSnapshot()
    {
        var state = Read("""
            {"base": {"name": "B", "dateMillisUtc": 1, "chars": [
              {"id": "a", "rank": 5, "rarity": 1, "stars": 3, "shards": 1, "mythicShards": 0,
               "activeAbilityLevel": 2, "passiveAbilityLevel": 3, "xpLevel": 4}
            ], "mows": [{"id": "m", "rarity": 0, "stars": 0, "primaryAbilityLevel": 1, "secondaryAbilityLevel": 1,
              "shards": 0, "mythicShards": 0, "locked": true}]},
             "diffs": [
              {"name": "D1", "dateMillisUtc": 2, "deletedDateMillisUtc": 9,
               "charDiffs": [{"id": "a", "rank": 6, "equip1": "item2", "equip1Level": 1}], "mowDiffs": [{"id": "m", "locked": false}]},
              {"name": "D2", "dateMillisUtc": 3, "charDiffs": [{"id": "a", "shards": 7}, {"id": "b", "rank": 2, "rarity": 0}], "mowDiffs": []},
              {"name": "D3", "dateMillisUtc": 4, "charDiffs": [{"id": "a", "equip1Level": 4}], "mowDiffs": [{"id": "m", "active": 5}]}
             ]}
            """);

        var snapshots = V1RosterSnapshotChain.Resolve(state);

        Assert.Equal(["B", "D1", "D2", "D3"], snapshots.Select(snapshot => snapshot.Name));
        Assert.True(snapshots[1].Deleted);
        var last = snapshots[3];
        var a = last.Characters.Single(unit => unit.Id == "a");
        Assert.Equal((6, 7L, 2, 3), (a.Rank, a.Shards, a.ActiveAbilityLevel, a.PassiveAbilityLevel));
        Assert.Equal(("item2", (int?)4), a.Equipment[1]);
        var b = last.Characters.Single(unit => unit.Id == "b");
        Assert.Equal((2, 0, 0), (b.Rank, b.Stars, b.XpLevel));
        var mow = Assert.Single(last.Mows);
        Assert.Equal((false, 5, 1), (mow.Locked, mow.PrimaryAbilityLevel, mow.SecondaryAbilityLevel));
    }

    [Fact]
    public void FixRaisesLevelsRarityAndStarsToTheirFloors()
    {
        var unit = new V1ResolvedCharacter("a", 5, 1, 0, 0, 0, 0, 0, 0, []);

        var fixedUnit = V1RosterSnapshotChain.Fix(unit, initialRarity: 2);

        Assert.Equal((2, 4, 1, 1, 1),
            (fixedUnit.Rarity, fixedUnit.Stars, fixedUnit.ActiveAbilityLevel, fixedUnit.PassiveAbilityLevel, fixedUnit.XpLevel));
    }

    [Theory]
    [InlineData("""{"diffs": []}""")]
    [InlineData("null")]
    public void NoBaseIsMissing(string json) =>
        Assert.False(TacticusV1Client.ReadRosterSnapshots(JsonDocument.Parse(json).RootElement).IsPresent);

    [Fact]
    public void AnUnreadableBlobIsInvalid()
    {
        var data = TacticusV1Client.ReadRosterSnapshots(JsonDocument.Parse("""{"base": {"chars": "x"}}""").RootElement);

        Assert.Equal((true, null), (data.IsPresent, data.State));
    }
}
