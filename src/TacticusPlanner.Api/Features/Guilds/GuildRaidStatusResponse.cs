using TacticusPlanner.Domain.GuildRaids.Enums;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed record GuildRaidStatusResponse(
    GuildRaidObservationState State,
    DateTimeOffset ObservedAt,
    GuildRaidFreshness Freshness,
    DateTimeOffset LastGuildSyncSucceededAt,
    GuildRaidSeasonStatusResponse? Season);

public sealed record GuildRaidSeasonStatusResponse(
    int SeasonNumber,
    string SeasonConfigId,
    DateTimeOffset? EndsAt,
    int TierIndex,
    int SetIndex,
    int SetCount,
    GuildRaidDifficulty Difficulty,
    GuildRaidBossStatusResponse Boss,
    IReadOnlyList<GuildRaidPrimeStatusResponse> Primes);

public sealed record GuildRaidBossStatusResponse(
    string UnitSetId,
    int ProgressionIndex,
    int RemainingHp,
    int MaximumHp,
    bool IsUpcoming);

public sealed record GuildRaidPrimeStatusResponse(
    int EncounterIndex,
    string UnitSetId,
    int ProgressionIndex,
    int? RemainingHp,
    int? MaximumHp,
    IReadOnlyList<GuildRaidModifierStatusResponse> Modifiers);

public sealed record GuildRaidModifierStatusResponse(
    string ModifierId,
    string Type,
    string Target,
    string? Subtarget,
    double Amount,
    int? ActivationRemainingHp,
    bool? Active);
