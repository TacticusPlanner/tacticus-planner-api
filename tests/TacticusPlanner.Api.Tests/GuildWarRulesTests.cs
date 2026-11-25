using TacticusPlanner.Api.Features.GuildWars;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// <see cref="GuildWarRules"/> over the shipped catalog (<c>guild-war-offense</c>, "A logged attack follows the war's
/// rules"): every rule code in order, the derived rarity cap and the war settings.
/// </summary>
public sealed class GuildWarRulesTests
{
    private static readonly GuildWarRuleFacts Facts = GuildWarCatalogFacts.For(GameCatalogLoader.Load());

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static GuildWar War(string seasonId = "guildWarSeasonA", int level = 5) =>
        GuildWar.Start(ProfileId.From(Guid.CreateVersion7()), seasonId, level, Now);

    private static GuildWarLineupUnit Unit(string id, UnitRank? rank = UnitRank.Gold1) => new()
    {
        UnitId = id,
        Progression = UnitProgression.EpicRedOneStar,
        Rank = rank,
        XpLevel = 40,
        AbilityLevels = [30, 30],
    };

    private static GuildWarAttackDraft Draft(
        string[] characters, string? mow = null, string zoneId = "Armoury", int slot = 2, GuildWarEnemy? enemy = null) =>
        new(zoneId, slot, new GuildWarLineup
        {
            Characters = [.. characters.Select(id => Unit(id))],
            Mow = mow is null ? null : Unit(mow, rank: null),
        }, enemy);

    private static GuildWarAttack Logged(GuildWar war, GuildWarAttackDraft draft) => new()
    {
        Id = GuildWarAttackId.From(Guid.CreateVersion7()),
        WarId = war.Id,
        ProfileId = war.ProfileId,
        AttackedAt = Now,
        ZoneId = draft.ZoneId,
        Slot = draft.Slot,
        RarityCap = GuildWarRules.RarityCap(Facts, war, draft.ZoneId, draft.Slot)!.Value,
        Lineup = draft.Lineup,
    };

    [Fact]
    public void AValidAttackBreaksNoRule() =>
        Assert.Null(GuildWarRules.Evaluate(Draft(["ultraCalgar", "ultraTitus"], "ultraDreadnought"), War(), null, Facts));

    [Fact]
    public void TheCapComesFromTheZoneTierAtTheWarsLevel()
    {
        Assert.Equal(GuildWarRarity.Epic, GuildWarRules.RarityCap(Facts, War(), "Armoury", 2));
        Assert.Equal(GuildWarRarity.Legendary, GuildWarRules.RarityCap(Facts, War(), "Armoury", 0));
    }

    [Theory]
    [InlineData("NoSuchZone", 0)]
    [InlineData("Armoury", 5)]
    [InlineData("Armoury", -1)]
    public void AnUnknownZoneOrSlotIsRefused(string zoneId, int slot) =>
        Assert.Equal(
            GuildWarAttackRuleCode.UnknownZone,
            GuildWarRules.Evaluate(Draft(["ultraCalgar"], zoneId: zoneId, slot: slot), War(), null, Facts));

    [Fact]
    public void AWarWhoseSeasonLeftTheCatalogRefusesEveryZone() =>
        Assert.Equal(
            GuildWarAttackRuleCode.UnknownZone,
            GuildWarRules.Evaluate(Draft(["ultraCalgar"]), War("guildWarSeasonGone"), null, Facts));

    public static TheoryData<GuildWarAttackDraft> UnknownUnits => new()
    {
        Draft(["noSuchCharacter"]),
        Draft(["ultraDreadnought"]),
        Draft(["ultraCalgar"], "ultraTitus"),
        Draft(["ultraCalgar", "ultraCalgar"]),
        Draft(["ultraCalgar"], enemy: new GuildWarEnemy { Characters = [new GuildWarEnemyUnit { UnitId = "noSuchCharacter" }] }),
        Draft(["ultraCalgar"], enemy: new GuildWarEnemy
        {
            Characters = [new GuildWarEnemyUnit { UnitId = "tauAunShi" }, new GuildWarEnemyUnit { UnitId = "tauAunShi" }],
        }),
        Draft(["ultraCalgar"], enemy: new GuildWarEnemy { BuffIds = ["NoSuchBuff"] }),
        Draft(["ultraCalgar"], enemy: new GuildWarEnemy { BuffIds = ["EnvArmourSupplies_Regional", "EnvArmourSupplies_Regional"] }),
        Draft(["ultraCalgar"], enemy: new GuildWarEnemy { BoardId = "noSuchBoard" }),
    };

    [Theory]
    [MemberData(nameof(UnknownUnits))]
    public void AnUnknownOrRepeatedUnitBuffOrBoardIsRefused(GuildWarAttackDraft draft) =>
        Assert.Equal(GuildWarAttackRuleCode.UnknownUnit, GuildWarRules.Evaluate(draft, War(), null, Facts));

    [Fact]
    public void KnownEnemyDetailsPass()
    {
        var season = GameCatalogLoader.Load().GuildWarViews.Single(season => season.Id == "guildWarSeasonA");
        var enemy = new GuildWarEnemy
        {
            Characters = [new GuildWarEnemyUnit { UnitId = "tauAunShi", Rarity = GuildWarRarity.Epic, State = GuildWarEnemyUnitState.Killed }],
            Mow = new GuildWarEnemyUnit { UnitId = "tauBroadside" },
            BuffIds = ["EnvArmourSupplies_Regional"],
            BoardId = season.Maps[0].BoardId,
        };

        Assert.Null(GuildWarRules.Evaluate(Draft(["ultraCalgar"], enemy: enemy), War(), null, Facts));
    }

    [Fact]
    public void AUnitAttacksOncePerWar()
    {
        var war = War();
        war.Attacks.Add(Logged(war, Draft(["ultraCalgar", "ultraTitus"], "ultraDreadnought")));

        Assert.Equal(GuildWarAttackRuleCode.UnitDeployed, GuildWarRules.Evaluate(Draft(["ultraCalgar"]), war, null, Facts));
        Assert.Equal(
            GuildWarAttackRuleCode.UnitDeployed,
            GuildWarRules.Evaluate(Draft(["bloodDante"], "ultraDreadnought"), war, null, Facts));
        Assert.Null(GuildWarRules.Evaluate(Draft(["bloodDante"]), war, null, Facts));
    }

    [Fact]
    public void AnUpdatedAttackDoesNotCountItsOwnUnitsOrToken()
    {
        var war = War();
        var attack = Logged(war, Draft(["ultraCalgar"]));
        war.Attacks.Add(attack);
        for (var index = 1; index < Facts.Seasons["guildWarSeasonA"].MaxAttempts; index++)
            war.Attacks.Add(Logged(war, Draft([])));

        Assert.Null(GuildWarRules.Evaluate(Draft(["ultraCalgar", "ultraTitus"]), war, attack.Id, Facts));
    }

    [Fact]
    public void TokensRunOutOnCreateOnly()
    {
        var war = War();
        var maxAttempts = Facts.Seasons["guildWarSeasonA"].MaxAttempts;
        for (var index = 0; index < maxAttempts; index++)
            war.Attacks.Add(Logged(war, Draft([])));

        Assert.Equal(GuildWarAttackRuleCode.TokensSpent, GuildWarRules.Evaluate(Draft(["ultraCalgar"]), war, null, Facts));
        Assert.Null(GuildWarRules.Evaluate(Draft(["ultraCalgar"]), war, war.Attacks[0].Id, Facts));
    }

    [Fact]
    public void RulesAreCheckedInOrder()
    {
        var war = War();
        war.Attacks.Add(Logged(war, Draft(["ultraCalgar"])));

        // Unknown zone wins over the deployed unit, the unknown unit over the deployed one.
        Assert.Equal(
            GuildWarAttackRuleCode.UnknownZone,
            GuildWarRules.Evaluate(Draft(["ultraCalgar"], zoneId: "NoSuchZone"), war, null, Facts));
        Assert.Equal(
            GuildWarAttackRuleCode.UnknownUnit,
            GuildWarRules.Evaluate(Draft(["ultraCalgar", "noSuchCharacter"]), war, null, Facts));
    }

    [Fact]
    public void AWarWhoseSeasonLeftTheCatalogHasTenTokens() =>
        Assert.Equal(GuildWarValidation.FallbackMaxAttempts, GuildWarRules.MaxAttempts(Facts, War("guildWarSeasonGone")));

    [Theory]
    [InlineData("guildWarSeasonA", 0, null)]
    [InlineData("guildWarSeasonD", 5, null)]
    [InlineData("guildWarSeasonGone", 1, GuildWarRules.UnknownSeason)]
    [InlineData("guildWarSeasonA", 6, GuildWarRules.UnknownLevel)]
    public void SettingsNameAServedSeasonAndOneOfItsLevels(string seasonId, int level, string? problem) =>
        Assert.Equal(problem, GuildWarRules.SettingsProblem(Facts, seasonId, level));

    [Fact]
    public void ShippedCatalogFacts()
    {
        Assert.Equal(["guildWarSeasonA", "guildWarSeasonB", "guildWarSeasonC", "guildWarSeasonD"], Facts.Seasons.Keys.Order());
        Assert.Contains("ultraCalgar", Facts.CharacterIds);
        Assert.Contains("ultraDreadnought", Facts.MowIds);
        Assert.Equal(10, Facts.Seasons["guildWarSeasonA"].MaxAttempts);
    }
}
