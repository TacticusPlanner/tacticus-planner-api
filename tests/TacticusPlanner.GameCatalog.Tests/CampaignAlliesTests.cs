using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class CampaignAlliesTests
{
    private static readonly string[] Imperial =
    [
        "AdeptusAstartes", "AdeptusMechanicus", "AstraMilitarum", "BlackTemplars", "BloodAngels",
        "Custodes", "DarkAngels", "Sisterhood", "SpaceWolves", "Ultramarines",
    ];

    private static readonly string[] Chaos =
        ["BlackLegion", "DeathGuard", "EmperorsChildren", "ThousandSons", "WorldEaters"];

    public static TheoryData<string, string, string[]> Expected => new()
    {
        { "campaign1", "Imperial", Imperial },
        { "elite1", "Imperial", Imperial },
        { "mirror1", "Xenos", ["Necrons"] },
        { "eliteMirror1", "Xenos", ["Necrons"] },
        { "campaign2", "Chaos", Chaos },
        { "elite2", "Chaos", Chaos },
        { "mirror2", "Imperial", Imperial },
        { "eliteMirror2", "Imperial", Imperial },
        { "campaign3", "Xenos", ["Orks"] },
        { "elite3", "Xenos", ["Orks"] },
        { "mirror3", "Imperial", Imperial },
        { "eliteMirror3", "Imperial", Imperial },
        { "campaign4", "Xenos", ["Aeldari"] },
        { "elite4", "Xenos", ["Aeldari"] },
        { "mirror4", "Chaos", Chaos },
        { "eliteMirror4", "Chaos", Chaos },
        { "eventCampaign1", "Chaos", ["DeathGuard", "WorldEaters"] },
        { "eventCampaign2", "Imperial", ["Ultramarines", "BloodAngels"] },
        { "eventCampaign3", "Xenos", ["Genestealers", "Tyranids"] },
        { "eventCampaign4", "Imperial", ["Sisterhood", "BlackTemplars"] },
        { "eventCampaign5", "Chaos", ["WorldEaters", "BlackLegion"] },
        { "eventCampaign6", "Xenos", ["Necrons"] },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void EveryBattleOfAGroupCarriesTheGroupAllies(string groupId, string alliance, string[] factions)
    {
        var battles = GameCatalogLoader.Load().CampaignBattleViews.Where(b => b.CampaignGroupId == groupId).ToArray();

        Assert.NotEmpty(battles);
        Assert.All(battles, battle =>
        {
            Assert.Equal(alliance, battle.AlliesAlliance);
            Assert.Equal(factions, battle.AlliesFactions);
        });
    }

    [Fact]
    public void EveryBattleHasAllies()
    {
        Assert.All(GameCatalogLoader.Load().CampaignBattleViews, battle =>
        {
            Assert.False(string.IsNullOrWhiteSpace(battle.AlliesAlliance));
            Assert.NotEmpty(battle.AlliesFactions);
        });
    }

    [Theory]
    [InlineData("", new[] { "Orks" }, "alliesAlliance")]
    [InlineData("Bogus", new[] { "Orks" }, "alliesAlliance")]
    [InlineData("Xenos", new string[0], "alliesFactions")]
    [InlineData("Xenos", new[] { "NoSuchFaction" }, "alliesFactions")]
    public void ValidatorRejectsBrokenAllies(string alliance, string[] factions, string field)
    {
        var snapshot = GameCatalogLoader.Load();
        var broken = snapshot.CampaignGroups.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.GroupId == "campaign3" ? pair.Value with { AlliesAlliance = alliance, AlliesFactions = factions } : pair.Value);

        var errors = GameCatalogValidator.Validate(snapshot with { CampaignGroups = broken });

        Assert.Contains(errors, e => e.Message.Contains("octarius") && e.Message.Contains(field));
    }
}
