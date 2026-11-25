using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Turns upstream Guild Raid entries into stored attacks, shared by the live refresh (which resolves progression
/// from the catalog and rejects a malformed entry) and past-season capture (which skips it).
/// </summary>
internal static class GuildRaidEntryNormalizer
{
    private const int UserIdSuffixLength = 4;

    private static bool IsValid(GuildRaidEntry entry) =>
        entry.UserId != Guid.Empty && entry.CompletedOn is not null && !string.IsNullOrWhiteSpace(entry.UnitId)
        && entry.RemainingHp >= 0 && entry.MaxHp >= 0;

    public static void Validate(GuildRaidEntry entry)
    {
        if (!IsValid(entry))
        {
            throw new InvalidOperationException("The Tacticus API returned an invalid Guild Raid entry.");
        }
    }

    public static (string UnitSetId, int? ProgressionIndex) ParseUnitId(string value) =>
        TryParseUnitId(value, out var unitSetId, out var progressionIndex)
            ? (unitSetId, progressionIndex)
            : throw new InvalidOperationException($"The Tacticus API returned invalid Guild Raid unit id '{value}'.");

    private static bool TryParseUnitId(
        string value,
        [NotNullWhen(true)] out string? unitSetId,
        out int? progressionIndex)
    {
        progressionIndex = null;
        var separator = value.LastIndexOf(':');
        if (separator < 0)
        {
            unitSetId = value;
            return true;
        }

        unitSetId = null;
        if (!int.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value[..separator]))
        {
            return false;
        }

        unitSetId = value[..separator];
        progressionIndex = parsed;
        return true;
    }

    /// <summary>
    /// The attack of a past season, from the entry alone: today's catalog may no longer have its position.
    /// False when the entry is malformed.
    /// </summary>
    public static bool TryBuildPastAttack(
        GuildRaidEntry entry,
        IColumnHashService hashService,
        [NotNullWhen(true)] out GuildRaidAttack? attack)
    {
        attack = null;
        if (!IsValid(entry) || !TryParseUnitId(entry.UnitId, out var unitSetId, out var progressionIndex))
        {
            return false;
        }

        attack = BuildAttack(entry, unitSetId, progressionIndex, BuildContentHash(entry), hashService);
        return true;
    }

    public static GuildRaidAttack BuildAttack(
        GuildRaidEntry entry,
        string unitSetId,
        int? progressionIndex,
        string contentHash,
        IColumnHashService hashService)
    {
        var attack = new GuildRaidAttack
        {
            Id = GuildRaidAttackId.From(Guid.CreateVersion7()),
            ContentHash = contentHash,
            TacticusUserIdHash = TacticusUserIdHash.From(hashService.ComputeHash(entry.UserId.ToString())!),
            TacticusUserIdSuffix = UserIdSuffix(entry.UserId),
            Tier = entry.Tier,
            Set = entry.Set,
            EncounterIndex = entry.EncounterIndex,
            RemainingHp = entry.RemainingHp,
            MaximumHp = entry.MaxHp,
            EncounterType = entry.EncounterType switch
            {
                EncounterType.SideBoss => GuildRaidEncounterType.SideBoss,
                EncounterType.Boss => GuildRaidEncounterType.Boss,
                _ => throw new InvalidOperationException($"Unknown Guild Raid encounter type '{entry.EncounterType}'."),
            },
            UnitSetId = unitSetId,
            ProgressionIndex = progressionIndex,
            Difficulty = entry.Rarity switch
            {
                Rarity.Common => GuildRaidDifficulty.Common,
                Rarity.Uncommon => GuildRaidDifficulty.Uncommon,
                Rarity.Rare => GuildRaidDifficulty.Rare,
                Rarity.Epic => GuildRaidDifficulty.Epic,
                Rarity.Legendary => GuildRaidDifficulty.Legendary,
                Rarity.Mythic => GuildRaidDifficulty.Mythic,
                _ => throw new InvalidOperationException($"Unknown Guild Raid rarity '{entry.Rarity}'."),
            },
            DamageDealt = entry.DamageDealt,
            DamageType = entry.DamageType switch
            {
                DamageType.Bomb => GuildRaidDamageType.Bomb,
                DamageType.Battle => GuildRaidDamageType.Battle,
                _ => throw new InvalidOperationException($"Unknown Guild Raid damage type '{entry.DamageType}'."),
            },
            StartedAt = entry.StartedOn is { } started
                ? DateTimeOffset.FromUnixTimeSeconds(started)
                : null,
            CompletedAt = DateTimeOffset.FromUnixTimeSeconds(entry.CompletedOn!.Value),
        };
        for (var index = 0; index < entry.HeroDetails.Count; index++)
        {
            attack.Units.Add(BuildUnit(entry.HeroDetails[index], GuildRaidAttackUnitKind.Hero, index));
        }
        if (entry.MachineOfWarDetails is { } mow)
        {
            attack.Units.Add(BuildUnit(mow, GuildRaidAttackUnitKind.MachineOfWar, 0));
        }

        return attack;
    }

    public static string UserIdSuffix(Guid userId) => userId.ToString()[^UserIdSuffixLength..];

    public static string BuildContentHash(GuildRaidEntry entry)
    {
        var canonical = string.Join('|',
            entry.UserId,
            entry.Tier,
            entry.Set,
            entry.EncounterIndex,
            entry.UnitId,
            entry.EncounterType,
            entry.RemainingHp,
            entry.MaxHp,
            entry.DamageDealt,
            entry.DamageType,
            entry.StartedOn,
            entry.CompletedOn,
            entry.GlobalConfigHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static GuildRaidAttackUnit BuildUnit(
        GuildRaidUnit unit,
        GuildRaidAttackUnitKind kind,
        int position) => new()
        {
            UnitId = unit.UnitId,
            Kind = kind,
            Position = position,
            Power = unit.Power,
        };
}
