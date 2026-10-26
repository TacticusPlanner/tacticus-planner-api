using TacticusPlanner.GameCatalog.Denormalization;
using TacticusPlanner.GameCatalog.Models;
using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class UnitDatamineReaderTests
{
    private static readonly Dictionary<string, DatamineAbility?> Abilities = new()
    {
        ["Strike"] = new(new Dictionary<string, string> { ["damageProfile"] = "Power", ["damageProfile_2"] = "Bolter", ["nrOfHits"] = "2" }, null),
        ["Aura"] = new(new Dictionary<string, string> { ["damageProfile"] = "Bolter" }, null),
        ["Buff"] = new(null, null),
        ["Beam"] = new(null, [["upgHpC001", "upgDmgC001", "upgArmC001"]]),
        ["Repair"] = new(null, [["upgHpC002", "upgDmgC002", "upgArmC002"]]),
        ["Placeholder"] = null,
    };

    private static DatamineLineupUnit Unit(
        string faction,
        IReadOnlyList<string> traits,
        IReadOnlyList<string> actives,
        IReadOnlyList<DatamineWeapon>? weapons = null,
        IReadOnlyList<IReadOnlyList<string>>? upgrades = null,
        IReadOnlyList<string>? mythics = null) =>
        new("Unit", faction, "Imperial", "Rare", 3, weapons, new DatamineLineupStats(100, 20, 15), traits, actives,
            ["Aura"], mythics, ["I_Crit", "I_Defensive", "I_Booster_Crit"], upgrades);

    [Fact]
    public void ACharacterMapsFromItsLineupEntry()
    {
        var units = GameCatalogDenormalizer.ReadDatamineUnits(
            new Dictionary<string, DatamineLineupUnit>
            {
                ["ultraHero"] = Unit("Ultramarines", ["Hero", "Psyker"], ["Strike"],
                    [new DatamineWeapon(3, "Physical", null), new DatamineWeapon(2, "Bolter", 2)],
                    [["a", "b", "c", "d", "e", "f"], ["g", "h", "i", "j", "k", "l"]]),
            },
            Abilities);

        var faction = units["units-ultramarines"];
        Assert.Equal(("Ultramarines", "Imperial"), (faction.FactionId, faction.Alliance));
        var character = Assert.Single(faction.Characters);
        Assert.Equal((100, 20, 15, "Rare", 3), (character.Health, character.Damage, character.Armour, character.InitialRarity, character.Movement));
        Assert.Equal(("Physical", 3, "Bolter", 2, 2),
            (character.MeleeDamage, character.MeleeHits, character.RangedDamage, character.RangedHits, character.RangeDistance));
        Assert.Equal(["Psyker"], character.Traits);
        Assert.Equal(["Bolter", "Power"], character.ActiveAbilityDamage);
        Assert.Equal(["Bolter"], character.PassiveAbilityDamage);
        Assert.Equal(["Stone1", "Stone2"], character.RankUpUpgrades.Select(rankUp => rankUp.Rank));
        Assert.Equal(["g", "h", "i", "j", "k", "l"], character.RankUpUpgrades[1].UpgradeIds);
        Assert.Empty(faction.Mows);
    }

    [Fact]
    public void AMachineOfWarTakesItsRecipesFromItsAbilities()
    {
        var units = GameCatalogDenormalizer.ReadDatamineUnits(
            new Dictionary<string, DatamineLineupUnit>
            {
                ["necroMow"] = Unit("Necrons", ["MachineOfWar"], ["Beam", "Repair"], mythics: ["Guardian"]),
            },
            Abilities);

        var mow = Assert.Single(units["units-necrons"].Mows);
        Assert.Equal(("Beam", "Repair", "Guardian"), (mow.PrimaryAbility.Name, mow.SecondaryAbility.Name, mow.MythicAbility?.Name));
        Assert.Equal(["upgHpC001", "upgDmgC001", "upgArmC001"], Assert.Single(mow.PrimaryAbility.Recipes));
        Assert.Equal(("Necrons", "Imperial", "Mow"), (mow.Faction, mow.Alliance, mow.UnitKind));
        Assert.Empty(units["units-necrons"].Characters);
    }

    [Fact]
    public void AMachineOfWarWithoutTwoActivesFailsTheLoad()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineUnits(
            new Dictionary<string, DatamineLineupUnit> { ["necroMow"] = Unit("Necrons", ["MachineOfWar"], ["Beam"]) },
            Abilities));

        Assert.Contains("'necroMow'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownAbilityFailsTheLoad()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineUnits(
            new Dictionary<string, DatamineLineupUnit> { ["ultraHero"] = Unit("Ultramarines", [], ["Missing"]) },
            Abilities));

        Assert.Contains("'Missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangedFirstWeaponFailsTheLoad()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameCatalogDenormalizer.ReadDatamineUnits(
            new Dictionary<string, DatamineLineupUnit>
            {
                ["ultraHero"] = Unit("Ultramarines", [], ["Buff"], [new DatamineWeapon(2, "Bolter", 2)]),
            },
            Abilities));

        Assert.Contains("'ultraHero'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LootObjectsGoToTheObjectsBucketWithDefaults()
    {
        var npcs = GameCatalogDenormalizer.ReadDatamineNpcs(
            new Dictionary<string, DatamineNpc>
            {
                ["LootObj_Crate"] = new("Crate", null, null, null, null, null, null, ["Placeholder"],
                    [new DatamineNpcStat(Health: 10, Damage: 0, FixedArmor: 0, Rank: 0, StarLevel: 0, AbilityLevel: 1, ProgressionIndex: 0)]),
                ["orksNpc"] = new("Boy", "Orks", "Xenos", 3, [new DatamineWeapon(2, "Physical", null)], ["Hero", "Mechanic"],
                    ["Strike"], [], []),
            },
            Abilities);

        Assert.Equal(["npcs-objects", "npcs-orks"], npcs.Keys);
        var objects = npcs["npcs-objects"];
        Assert.Equal(("Objects", "Neutral"), (objects.FactionId, objects.Alliance));
        var crate = Assert.Single(objects.Npcs);
        Assert.Equal(("", 0, 0, (string?)null), (crate.MeleeDamage, crate.MeleeHits, crate.Movement, crate.RangedDamage));
        Assert.Empty(crate.PassiveAbilityDamage);
        Assert.Equal(new GameCatalogNpcStat(1, 0, 0, 10, 0, 0, 0), Assert.Single(crate.Stats));
        var boy = Assert.Single(npcs["npcs-orks"].Npcs);
        Assert.Equal(["Mechanic"], boy.Traits);
        Assert.Equal(["Bolter", "Power"], boy.ActiveAbilityDamage);
    }

    [Fact]
    public void TheShippedLineupAndNpcsLoad()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Equal(119, snapshot.Characters.Count);
        Assert.Equal(11, snapshot.Mows.Count);
        Assert.Equal(539, snapshot.Npcs.Count);
        Assert.Equal(3, snapshot.Npcs.Single(npc => npc.Id == "votanNpc2Steeljack").Movement);
    }
}
