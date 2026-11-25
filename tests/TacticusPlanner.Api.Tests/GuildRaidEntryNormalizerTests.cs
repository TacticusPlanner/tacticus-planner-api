using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.Tests;

public sealed class GuildRaidEntryNormalizerTests
{
    private static readonly Guid Player = Guid.Parse("a6977954-4da1-4218-b939-accdef523bc4");

    [Theory]
    [InlineData("guildBossAvatarMortarion:3", "guildBossAvatarMortarion", 3)]
    [InlineData("guildBossAvatarMortarion", "guildBossAvatarMortarion", null)]
    public void PastAttackKeepsItsOwnProgression(string unitId, string unitSetId, int? progression)
    {
        var entry = Entry();
        entry.UnitId = unitId;

        Assert.True(GuildRaidEntryNormalizer.TryBuildPastAttack(entry, new Hash(), out var attack));
        Assert.Equal(unitSetId, attack.UnitSetId);
        Assert.Equal(progression, attack.ProgressionIndex);
        Assert.Equal("3bc4", attack.TacticusUserIdSuffix);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void PastAttackKeepsTheLoopCountingTier(int tier)
    {
        var entry = Entry();
        entry.Tier = tier;

        Assert.True(GuildRaidEntryNormalizer.TryBuildPastAttack(entry, new Hash(), out var attack));
        Assert.Equal(tier, attack.Tier);
    }

    public static TheoryData<string> MalformedEntries => ["player", "completion", "unit", "suffix", "zeroSuffix", "onlySuffix", "blankPrefix", "remaining", "maximum"];

    [Theory]
    [MemberData(nameof(MalformedEntries))]
    public void MalformedPastEntryIsSkipped(string defect)
    {
        var entry = Entry();
        switch (defect)
        {
            case "player": entry.UserId = Guid.Empty; break;
            case "completion": entry.CompletedOn = null; break;
            case "unit": entry.UnitId = " "; break;
            case "suffix": entry.UnitId = "boss:x"; break;
            case "zeroSuffix": entry.UnitId = "boss:0"; break;
            case "onlySuffix": entry.UnitId = ":3"; break;
            case "blankPrefix": entry.UnitId = " :3"; break;
            case "remaining": entry.RemainingHp = -1; break;
            case "maximum": entry.MaxHp = -1; break;
        }

        Assert.False(GuildRaidEntryNormalizer.TryBuildPastAttack(entry, new Hash(), out _));
    }

    private static GuildRaidEntry Entry() => new()
    {
        UserId = Player,
        UnitId = "guildBossAvatarMortarion:3",
        EncounterType = EncounterType.Boss,
        Rarity = Rarity.Mythic,
        DamageType = DamageType.Battle,
        CompletedOn = 1_780_000_100,
    };

    private sealed class Hash : IColumnHashService
    {
        public byte[]? ComputeHash(string? value) => new byte[32];
    }
}
