using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed class GuildRaidAttackRepository(PlannerDbContext db)
{
    public async Task<IReadOnlyList<CurrentUserGuildRaidAttack>> GetCurrentUserAttacksAsync(
        GuildRaidSeasonId seasonId,
        TacticusUserIdHash tacticusUserIdHash,
        bool includeUnits,
        CancellationToken ct)
    {
        var rows = await QueryCurrentUserAttacks(seasonId, tacticusUserIdHash)
            .ToListAsync(ct);
        var attacks = rows.Select(attack => new CurrentUserGuildRaidAttack(
                attack.Id,
                attack.CompletedAt,
                attack.UnitSetId,
                attack.EncounterType,
                attack.RemainingHp,
                attack.MaximumHp,
                Array.Empty<CurrentUserGuildRaidAttackUnit>()))
            .ToList();
        if (!includeUnits || attacks.Count == 0)
        {
            return attacks;
        }

        var attackIds = attacks.Select(attack => attack.Id).ToArray();
        var units = await db.GuildRaidAttackUnits.AsNoTracking()
            .Where(unit => attackIds.Contains(unit.GuildRaidAttackId))
            .OrderBy(unit => unit.Kind).ThenBy(unit => unit.Position)
            .Select(unit => new { unit.GuildRaidAttackId, Item = new CurrentUserGuildRaidAttackUnit(unit.UnitId, unit.Kind, unit.Position, unit.Power) })
            .ToListAsync(ct);
        var byAttack = units.GroupBy(unit => unit.GuildRaidAttackId).ToDictionary(group => group.Key, group => (IReadOnlyList<CurrentUserGuildRaidAttackUnit>)group.Select(unit => unit.Item).ToArray());
        return attacks.Select(attack => attack with { Units = byAttack.GetValueOrDefault(attack.Id, Array.Empty<CurrentUserGuildRaidAttackUnit>()) }).ToArray();
    }

    public IQueryable<CurrentUserGuildRaidAttackRow> QueryCurrentUserAttacks(
        GuildRaidSeasonId seasonId,
        TacticusUserIdHash tacticusUserIdHash) =>
        db.GuildRaidAttacks.AsNoTracking()
            .Where(attack => attack.GuildRaidSeasonId == seasonId && attack.TacticusUserIdHash == tacticusUserIdHash)
            .OrderBy(attack => attack.CompletedAt)
            .Select(attack => new CurrentUserGuildRaidAttackRow(
                attack.Id,
                attack.CompletedAt,
                attack.UnitSetId,
                attack.EncounterType,
                attack.RemainingHp,
                attack.MaximumHp));
}

public sealed record CurrentUserGuildRaidAttackRow(
    GuildRaidAttackId Id,
    DateTimeOffset CompletedAt,
    string UnitSetId,
    GuildRaidEncounterType EncounterType,
    int RemainingHp,
    int MaximumHp);

public sealed record CurrentUserGuildRaidAttack(
    GuildRaidAttackId Id,
    DateTimeOffset CompletedAt,
    string UnitSetId,
    GuildRaidEncounterType EncounterType,
    int RemainingHp,
    int MaximumHp,
    IReadOnlyList<CurrentUserGuildRaidAttackUnit> Units);

public sealed record CurrentUserGuildRaidAttackUnit(
    string UnitId,
    GuildRaidAttackUnitKind Kind,
    int Position,
    int Power);
