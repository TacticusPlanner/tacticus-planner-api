using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class MythicQuestValidationTests
{
    private static readonly HashSet<string> CharacterIds = new(StringComparer.Ordinal) { "blackAbaddon", "tauMarksman" };
    private static readonly HashSet<string> EquipmentIds = new(StringComparer.Ordinal) { "I_Crit_M006" };

    private static GameCatalogRawMythicQuest Quest(int number, string reward = "gold:1", int target = 1, bool withTask = true) => new(
        number,
        [reward],
        withTask
            ? [new GameCatalogRawMythicQuestTask("DealDamageWithHero_description", target, new Dictionary<string, string> { ["heroId"] = "blackAbaddon" })]
            : []);

    private static GameCatalogRawMythicQuests Character(string id = "blackAbaddon", Func<int, GameCatalogRawMythicQuest>? quest = null) =>
        new(id, Enumerable.Range(1, 5).Select(number => (quest ?? (n => Quest(n)))(number)).ToArray());

    private static List<GameCatalogValidationError> Validate(params GameCatalogRawMythicQuests[] raw)
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateMythicQuests(raw, CharacterIds, EquipmentIds, errors);
        return errors;
    }

    private static void AssertSingleError(List<GameCatalogValidationError> errors, string code)
    {
        var error = Assert.Single(errors);
        Assert.Equal(GameCatalogDatasets.MythicQuests, error.Dataset);
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void ShippedSourceLoadsIncludingASummonAsTheTaskHero()
    {
        var snapshot = GameCatalogLoader.Load();
        var marksmanTask = snapshot.MythicQuests.Single(character => character.CharacterId == "tauMarksman").Quests[0].Tasks[0];

        Assert.Equal("tauSmnDroneSniper", marksmanTask.TaskParameters["heroId"]);
        Assert.Contains(GameCatalogDatasets.MythicQuestsServed, snapshot.DatasetHashes.Keys);
    }

    [Fact]
    public void ValidSourceHasNoErrors() => Assert.Empty(Validate(Character()));

    [Fact]
    public void EmptySourceFails() => AssertSingleError(Validate(), "EmptyDataset");

    [Fact]
    public void UnknownCharacterFails() => AssertSingleError(Validate(Character("weirdboy")), "MissingReference");

    [Fact]
    public void QuestNumbersOtherThanOneToFiveFail() =>
        AssertSingleError(Validate(new GameCatalogRawMythicQuests("blackAbaddon", [Quest(1), Quest(2), Quest(3), Quest(4), Quest(6)])), "InvalidQuestNumbers");

    [Fact]
    public void QuestWithoutTasksFails() =>
        AssertSingleError(Validate(Character(quest: n => Quest(n, withTask: n != 3))), "MissingTasks");

    [Fact]
    public void TaskTargetBelowOneFails() =>
        AssertSingleError(Validate(Character(quest: n => Quest(n, target: n == 2 ? 0 : 1))), "InvalidTarget");

    [Fact]
    public void UnknownRewardPatternFails() =>
        AssertSingleError(Validate(Character(quest: n => Quest(n, reward: n == 1 ? "blueStarMythic:1" : "gold:1"))), "InvalidReward");

    [Fact]
    public void UnknownEquipmentRewardFails() =>
        AssertSingleError(Validate(Character(quest: n => Quest(n, reward: n == 5 ? "I_Crit_M999" : "gold:1"))), "MissingReference");

    [Fact]
    public void UnknownShardUnitFails() =>
        AssertSingleError(Validate(Character(quest: n => Quest(n, reward: n == 1 ? "mythicShards_weirdboy:4" : "gold:1"))), "MissingReference");

    [Fact]
    public void HeroIdIsNotCheckedAgainstTheCatalog()
    {
        var quest = new GameCatalogRawMythicQuest(
            1,
            ["gold:1"],
            [new GameCatalogRawMythicQuestTask("ScoreHitsWithHeroInOneTurn_description", 1, new Dictionary<string, string> { ["heroId"] = "tauSmnDroneSniper" })]);

        Assert.Empty(Validate(Character("tauMarksman", n => n == 1 ? quest : Quest(n))));
    }
}
