using System.Text.Json;
using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameCatalog.Validation;
using Xunit;
using MissionTuple = (int Index, string Kind, int Count, string? Faction, string? Alliance, string? DamageType, string? Trait);

namespace TacticusPlanner.GameCatalog.Tests;

/// <summary>Covers <c>add-legendary-event-mission-tracking</c>'s <c>legendary-event-missions-dataset</c>
/// capability: served <c>lres</c> missions are typed records parsed from the raw lines, and the loader rejects a
/// line no template matches or a label with no catalog mapping.</summary>
public sealed class LreMissionsTests
{
    private static readonly GameCatalogSnapshot Snapshot = GameCatalogLoader.Load();

    private static GameCatalogLreView View(string id) => Snapshot.LreViews.Single(view => view.Id == id);

    private static GameCatalogLre Raw(string id) => Snapshot.Lres.Single(lre => lre.UnitSnowprintId == id);

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly string[] Kinds =
    [
        LreMissionKinds.AnyBattle,
        LreMissionKinds.DefeatFaction,
        LreMissionKinds.UseAbilitiesWithAlliance,
        LreMissionKinds.DamageType,
        LreMissionKinds.DefeatWithAlliance,
        LreMissionKinds.WinWithoutTrait,
        LreMissionKinds.WinWithoutSummon,
        LreMissionKinds.DefeatWithDamageType,
    ];

    [Fact]
    public void UtharMissionsMatchTheSpec()
    {
        var uthar = View("votanUthar");

        Assert.Equal(
            new GameCatalogLreMission(1, "defeatFaction", 30, "Necrons", null, null, null, "Defeat 30 Necrons"),
            uthar.RegularMissions[1]);
        Assert.Equal(("damageType", 15, "Bolter"), (uthar.RegularMissions[9].Kind, uthar.RegularMissions[9].Count, uthar.RegularMissions[9].DamageType));
        Assert.Equal(("winWithoutTrait", 5, "BigTarget"), (uthar.PremiumMissions[4].Kind, uthar.PremiumMissions[4].Count, uthar.PremiumMissions[4].Trait));
    }

    [Fact]
    public void EveryUtharMissionParsesToItsKindAndParameters()
    {
        var uthar = View("votanUthar");

        MissionTuple[] regular =
            [
                (0, "anyBattle", 5, null, null, null, null),
                (1, "defeatFaction", 30, "Necrons", null, null, null),
                (2, "defeatFaction", 75, "AdeptusMechanicus", null, null, null),
                (3, "defeatFaction", 100, "DeathGuard", null, null, null),
                (4, "useAbilitiesWithAlliance", 15, null, "Imperial", null, null),
                (5, "useAbilitiesWithAlliance", 20, null, "Chaos", null, null),
                (6, "useAbilitiesWithAlliance", 25, null, "Xenos", null, null),
                (7, "damageType", 15, null, null, "Piercing", null),
                (8, "damageType", 15, null, null, "Psychic", null),
                (9, "damageType", 15, null, null, "Bolter", null),
            ];
        MissionTuple[] premium =
            [
                (0, "winWithoutSummon", 1, null, null, null, null),
                (1, "defeatWithAlliance", 30, null, "Chaos", null, null),
                (2, "defeatWithAlliance", 75, null, "Imperial", null, null),
                (3, "defeatWithAlliance", 100, null, "Xenos", null, null),
                (4, "winWithoutTrait", 5, null, null, null, "BigTarget"),
                (5, "winWithoutTrait", 5, null, null, null, "Resilient"),
                (6, "winWithoutTrait", 5, null, null, null, "Ambush"),
                (7, "defeatWithDamageType", 75, null, null, "Bolter", null),
                (8, "defeatWithDamageType", 75, null, null, "Power", null),
                (9, "defeatWithDamageType", 75, null, null, "Piercing", null),
            ];

        Assert.Equal(regular, uthar.RegularMissions.Select(Tuple));
        Assert.Equal(premium, uthar.PremiumMissions.Select(Tuple));
    }

    [Fact]
    public void EveryEventsFirstRegularMissionIsAnyBattleOfFiveWaves()
    {
        Assert.Equal(4, Snapshot.LreViews.Count);
        Assert.All(Snapshot.LreViews, view =>
        {
            var first = view.RegularMissions[0];
            Assert.Equal("Defeat 5 waves of enemies in any wave-based game mode, Deal 1000 damage", first.Text);
            Assert.Equal((0, "anyBattle", 5), (first.Index, first.Kind, first.Count));
        });
    }

    [Fact]
    public void EveryRawMissionLineIsServedTypedInRawOrder()
    {
        var errors = new List<GameCatalogValidationError>();
        GameCatalogValidator.ValidateLreMissions(Snapshot.Lres, errors);
        Assert.Empty(errors);

        foreach (var raw in Snapshot.Lres)
        {
            var view = View(raw.UnitSnowprintId);
            foreach (var (lines, missions) in new[] { (raw.RegularMissions, view.RegularMissions), (raw.PremiumMissions, view.PremiumMissions) })
            {
                Assert.Equal(10, missions.Count);
                Assert.Equal(lines, missions.Select(mission => mission.Text));
                Assert.Equal(Enumerable.Range(0, lines.Count), missions.Select(mission => mission.Index));
                Assert.All(missions, mission =>
                {
                    Assert.Contains(mission.Kind, Kinds);
                    Assert.True(mission.Count > 0, mission.Text);
                    AssertOnlyItsParametersAreSet(mission);
                });
            }
        }
    }

    [Fact]
    public void AllEightKindsAppearInTheShippedEvents()
    {
        var served = Snapshot.LreViews
            .SelectMany(view => view.RegularMissions.Concat(view.PremiumMissions))
            .Select(mission => mission.Kind)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(Kinds.Order(StringComparer.Ordinal), served.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DaemonEnemiesIsATraitGroupNotAFaction()
    {
        var mission = View("lostaBile").RegularMissions[1];

        Assert.Equal(
            new GameCatalogLreMission(1, "defeatFaction", 30, null, null, null, "Daemon", "Defeat 30 Daemon enemies"),
            mission);
    }

    [Theory]
    [InlineData("Defeat 75 T'au", "Tau")]
    [InlineData("Defeat 30 AdMechs", "AdeptusMechanicus")]
    [InlineData("Defeat 100 Black Legion", "BlackLegion")]
    [InlineData("Defeat 100 Dark Angels", "DarkAngels")]
    public void FactionLabelsMapToCatalogFactionIds(string text, string faction)
    {
        var mission = GameCatalogDenormalizer.ParseLreMission(0, text, out var problem);

        Assert.Null(problem);
        Assert.Equal(("defeatFaction", faction), (mission!.Kind, mission.Faction));
    }

    [Fact]
    public void FinalVengeanceIsTheFinalJusticeTrait()
    {
        var mission = View("lostaBile").PremiumMissions[4];

        Assert.Equal(("winWithoutTrait", "FinalJustice"), (mission.Kind, mission.Trait));
    }

    [Fact]
    public void AliasTargetsAreCatalogIds()
    {
        var units = Snapshot.UnitsByFaction.Values.ToArray();
        var npcs = Snapshot.NpcsByFaction.Values.ToArray();
        var factions = units.Select(faction => faction.FactionId).Concat(npcs.Select(faction => faction.FactionId)).ToHashSet(StringComparer.Ordinal);
        var alliances = units.Select(faction => faction.Alliance).ToHashSet(StringComparer.Ordinal);
        var damageTypes = units.SelectMany(faction => faction.Characters)
            .SelectMany(character => new[] { character.MeleeDamage, character.RangedDamage }
                .Concat(character.ActiveAbilityDamage).Concat(character.PassiveAbilityDamage))
            .Concat(npcs.SelectMany(faction => faction.Npcs)
                .SelectMany(npc => new[] { npc.MeleeDamage, npc.RangedDamage }
                    .Concat(npc.ActiveAbilityDamage).Concat(npc.PassiveAbilityDamage)))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Subset(factions, GameCatalogDenormalizer.LreMissionFactionAliases.Values.ToHashSet(StringComparer.Ordinal));
        Assert.Subset(alliances, GameCatalogDenormalizer.LreMissionAllianceAliases.Values.ToHashSet(StringComparer.Ordinal));
        Assert.Subset(damageTypes, GameCatalogDenormalizer.LreMissionDamageTypeAliases.Values.ToHashSet(StringComparer.Ordinal));
        Assert.Subset(EnumTraitIds(), GameCatalogDenormalizer.LreMissionTraitAliases.Values.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void ANewTemplateFailsNamingTheFileListAndLine()
    {
        var raw = Raw("votanUthar");
        var premium = raw.PremiumMissions.ToArray();
        premium[3] = "Win 3 battles with only Tyranids";
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreMissions([raw with { PremiumMissions = premium }], errors);

        var error = Assert.Single(errors);
        Assert.Equal((GameCatalogDatasets.LresPrefix, "UnparsedMission"), (error.Dataset, error.Code));
        Assert.Contains("votanUthar", error.Message, StringComparison.Ordinal);
        Assert.Contains(raw.SourceFile, error.Message, StringComparison.Ordinal);
        Assert.Contains("premiumMissions[3]", error.Message, StringComparison.Ordinal);
        Assert.Contains("Win 3 battles with only Tyranids", error.Message, StringComparison.Ordinal);
        Assert.Contains("no mission template", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Defeat 30 Squats", "faction 'Squats'")]
    [InlineData("Defeat 30 enemies with Neutral units", "alliance 'Neutral'")]
    [InlineData("Defeat 75 enemies with Warp Damage", "damage type 'Warp'")]
    [InlineData("Win 5 battles without deploying any Tall characters", "trait 'Tall'")]
    public void AnUnknownAliasFailsNamingTheLabel(string line, string expected)
    {
        var raw = Raw("votanUthar");
        var regular = raw.RegularMissions.ToArray();
        regular[2] = line;
        var errors = new List<GameCatalogValidationError>();

        GameCatalogValidator.ValidateLreMissions([raw with { RegularMissions = regular }], errors);

        var error = Assert.Single(errors);
        Assert.Contains(raw.SourceFile, error.Message, StringComparison.Ordinal);
        Assert.Contains("regularMissions[2]", error.Message, StringComparison.Ordinal);
        Assert.Contains(line, error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatalogValidatorRunsTheMissionCheck()
    {
        var raw = Raw("votanUthar");
        var premium = raw.PremiumMissions.ToArray();
        premium[0] = "Win 3 battles with only Tyranids";
        var broken = Snapshot.LresByEvent.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.UnitSnowprintId == "votanUthar" ? raw with { PremiumMissions = premium } : pair.Value);

        var errors = GameCatalogValidator.Validate(Snapshot with { LresByEvent = broken });

        Assert.Contains(errors, error => error.Code == "UnparsedMission"
            && error.Message.Contains("votanUthar", StringComparison.Ordinal)
            && error.Message.Contains("premiumMissions[0]", StringComparison.Ordinal));
    }

    [Fact]
    public void ServedMissionJsonUsesTheContractFieldNamesWithNullParameters()
    {
        var json = JsonSerializer.SerializeToElement(View("votanUthar").RegularMissions[1], WebJson);

        Assert.Equal(
            ["index", "kind", "count", "faction", "alliance", "damageType", "trait", "text"],
            json.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("trait").ValueKind);
    }

    private static MissionTuple Tuple(GameCatalogLreMission mission) =>
        (mission.Index, mission.Kind, mission.Count, mission.Faction, mission.Alliance, mission.DamageType, mission.Trait);

    private static void AssertOnlyItsParametersAreSet(GameCatalogLreMission mission)
    {
        var set = (mission.Faction is not null, mission.Alliance is not null, mission.DamageType is not null, mission.Trait is not null);
        var expected = mission.Kind switch
        {
            LreMissionKinds.AnyBattle or LreMissionKinds.WinWithoutSummon => (false, false, false, false),
            LreMissionKinds.DefeatFaction => mission.Trait is null ? (true, false, false, false) : (false, false, false, true),
            LreMissionKinds.UseAbilitiesWithAlliance or LreMissionKinds.DefeatWithAlliance => (false, true, false, false),
            LreMissionKinds.DamageType or LreMissionKinds.DefeatWithDamageType => (false, false, true, false),
            _ => (false, false, false, true),
        };
        Assert.True(expected == set, $"{mission.Kind}: {mission.Text}");
    }

    private static HashSet<string> EnumTraitIds()
    {
        var assembly = typeof(GameCatalogLoader).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith(".enums.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("traits").EnumerateArray()
            .Select(trait => trait.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
