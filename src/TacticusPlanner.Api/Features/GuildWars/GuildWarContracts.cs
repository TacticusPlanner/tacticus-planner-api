using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Features.GuildWars;

/// <summary>A war as every war endpoint returns it (<c>guild-war-offense</c>). Ids and codes only. Tokens used and
/// deployed units are derived from the attacks (design D7).</summary>
public sealed record GuildWarResponse(
    Guid Id,
    long Revision,
    string SeasonId,
    int BattlefieldLevel,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TokensUsed,
    IReadOnlyList<string> DeployedUnitIds,
    IReadOnlyList<GuildWarAttackResponse> Attacks
)
{
    public static GuildWarResponse From(GuildWar war)
    {
        var attacks = war.OrderedAttacks().Select(GuildWarAttackResponse.From).ToList();
        return new GuildWarResponse(
            war.Id.Value, war.Revision, war.SeasonId, war.BattlefieldLevel, war.StartedAt, war.EndedAt, attacks.Count,
            war.DeployedUnitIds(), attacks);
    }
}

public sealed record GuildWarAttackResponse(
    Guid Id,
    long Revision,
    DateTimeOffset AttackedAt,
    string ZoneId,
    int Slot,
    GuildWarRarity RarityCap,
    IReadOnlyList<GuildWarLineupUnitDto> Characters,
    GuildWarLineupUnitDto? Mow,
    GuildWarAttackResult? Result,
    IReadOnlyList<string>? LostUnitIds,
    GuildWarEnemyDto? Enemy,
    string? Notes
)
{
    public static GuildWarAttackResponse From(GuildWarAttack attack) => new(
        attack.Id.Value,
        attack.Revision,
        attack.AttackedAt,
        attack.ZoneId,
        attack.Slot,
        attack.RarityCap,
        [.. attack.Lineup.Characters.Select(GuildWarLineupUnitDto.From)],
        attack.Lineup.Mow is { } mow ? GuildWarLineupUnitDto.From(mow) : null,
        attack.Result,
        attack.LostUnitIds,
        attack.Enemy is { } enemy ? GuildWarEnemyDto.From(enemy) : null,
        attack.Notes);
}

/// <summary>A lineup unit as the player had it; <c>rank</c> is null for a MoW.</summary>
public sealed record GuildWarLineupUnitDto(
    string UnitId,
    UnitProgression Progression,
    UnitRank? Rank,
    int XpLevel,
    IReadOnlyList<int> AbilityLevels
)
{
    public static GuildWarLineupUnitDto From(GuildWarLineupUnit unit) =>
        new(unit.UnitId, unit.Progression, unit.Rank, unit.XpLevel, unit.AbilityLevels);

    public GuildWarLineupUnit ToDomain() => new()
    {
        UnitId = UnitId,
        Progression = Progression,
        Rank = Rank,
        XpLevel = XpLevel,
        AbilityLevels = [.. AbilityLevels],
    };
}

/// <summary>Optional enemy details of an attack.</summary>
public sealed record GuildWarEnemyDto(
    IReadOnlyList<GuildWarEnemyUnitDto> Characters,
    GuildWarEnemyUnitDto? Mow,
    IReadOnlyList<string> BuffIds,
    string? BoardId
)
{
    public static GuildWarEnemyDto From(GuildWarEnemy enemy) => new(
        [.. enemy.Characters.Select(GuildWarEnemyUnitDto.From)],
        enemy.Mow is { } mow ? GuildWarEnemyUnitDto.From(mow) : null,
        enemy.BuffIds,
        enemy.BoardId);

    public GuildWarEnemy ToDomain() => new()
    {
        Characters = [.. Characters.Select(unit => unit.ToDomain())],
        Mow = Mow?.ToDomain(),
        BuffIds = [.. BuffIds],
        BoardId = BoardId,
    };
}

public sealed record GuildWarEnemyUnitDto(
    string UnitId,
    GuildWarRarity? Rarity,
    UnitRank? Rank,
    GuildWarEnemyUnitState? State
)
{
    public static GuildWarEnemyUnitDto From(GuildWarEnemyUnit unit) => new(unit.UnitId, unit.Rarity, unit.Rank, unit.State);

    public GuildWarEnemyUnit ToDomain() => new() { UnitId = UnitId, Rarity = Rarity, Rank = Rank, State = State };
}

/// <summary>The current war, or null when the profile has none.</summary>
public sealed record CurrentGuildWarResponse(GuildWarResponse? War);

/// <summary>A page of ended wars, newest first; <c>nextAfter</c> continues the list, null on the last page.</summary>
public sealed record PastGuildWarsResponse(IReadOnlyList<GuildWarResponse> Items, string? NextAfter);

/// <summary>Sets the current war's season and battlefield level, creating the war when there is none (then
/// <c>revision</c> is ignored).</summary>
public sealed record SetCurrentGuildWarRequest(string SeasonId, int BattlefieldLevel, long Revision);

/// <summary>Ends the current war (kept as history) and starts a new one. <c>currentWarId</c> is the war the caller
/// means to end, or null when it sees none, so a retried request does not end the war the first one started.</summary>
public sealed record StartGuildWarRequest(string SeasonId, int BattlefieldLevel, Guid? CurrentWarId);

public sealed class ListGuildWarsRequest
{
    /// <summary>Page size, 1–50 (default 20).</summary>
    [QueryParam]
    public int? Limit { get; init; }

    /// <summary>The opaque <c>nextAfter</c> of the previous page.</summary>
    [QueryParam]
    public string? After { get; init; }
}

/// <summary>The fields of an attack, shared by create and update.</summary>
public interface IGuildWarAttackFields
{
    string ZoneId { get; }
    int Slot { get; }
    IReadOnlyList<GuildWarLineupUnitDto> Characters { get; }
    GuildWarLineupUnitDto? Mow { get; }
    GuildWarAttackResult? Result { get; }
    IReadOnlyList<string>? LostUnitIds { get; }
    GuildWarEnemyDto? Enemy { get; }
    string? Notes { get; }
}

internal static class GuildWarAttackFieldsExtensions
{
    public static GuildWarLineup ToLineup(this IGuildWarAttackFields fields) => new()
    {
        Characters = [.. fields.Characters.Select(unit => unit.ToDomain())],
        Mow = fields.Mow?.ToDomain(),
    };

    public static GuildWarAttackDraft ToDraft(this IGuildWarAttackFields fields) =>
        new(fields.ZoneId, fields.Slot, fields.ToLineup(), fields.Enemy?.ToDomain());
}

/// <summary>Logs an attack in the current war; <c>attackedAt</c> defaults to now.</summary>
public sealed record CreateGuildWarAttackRequest(
    string ZoneId,
    int Slot,
    IReadOnlyList<GuildWarLineupUnitDto> Characters,
    GuildWarLineupUnitDto? Mow,
    GuildWarAttackResult? Result,
    IReadOnlyList<string>? LostUnitIds,
    GuildWarEnemyDto? Enemy,
    string? Notes,
    DateTimeOffset? AttackedAt
) : IGuildWarAttackFields;

/// <summary>Replaces an attack's fields when <see cref="Revision"/> matches; <c>attackedAt</c> is kept.</summary>
public sealed record UpdateGuildWarAttackRequest(
    string ZoneId,
    int Slot,
    IReadOnlyList<GuildWarLineupUnitDto> Characters,
    GuildWarLineupUnitDto? Mow,
    GuildWarAttackResult? Result,
    IReadOnlyList<string>? LostUnitIds,
    GuildWarEnemyDto? Enemy,
    string? Notes,
    long Revision
) : IGuildWarAttackFields;

/// <summary>Shape checks every war and attack write passes before the war rules run.</summary>
internal static class GuildWarFieldsValidation
{
    public static void ApplySettings<T>(AbstractValidator<T> validator, Func<T, string> seasonId, Func<T, int> level)
    {
        validator.RuleFor(request => seasonId(request)).NotEmpty().MaximumLength(GuildWarValidation.MaxIdLength)
            .OverridePropertyName("seasonId");
        // The upper bound is the season's own levels (guildWarUnknownLevel), so a new level needs no code change.
        validator.RuleFor(request => level(request)).GreaterThanOrEqualTo(0)
            .OverridePropertyName("battlefieldLevel");
    }

    public static void ApplyAttack<T>(AbstractValidator<T> validator)
        where T : IGuildWarAttackFields
    {
        validator.RuleFor(request => request.ZoneId).NotEmpty().MaximumLength(GuildWarValidation.MaxIdLength);
        validator.RuleFor(request => request.Slot).InclusiveBetween(0, GuildWarValidation.Slots - 1);
        validator.RuleFor(request => request.Characters).Cascade(CascadeMode.Stop).NotNull()
            .Must(units => units.Count is >= 1 and <= GuildWarValidation.MaxLineupCharacters)
            .WithMessage($"An attack has 1 to {GuildWarValidation.MaxLineupCharacters} characters.")
            .Must(units => units.All(unit => ValidLineupUnit(unit) && unit.Rank is { } rank && Enum.IsDefined(rank)))
            .WithMessage("Each lineup character needs an id, a known progression and rank, an XP level and at most two ability levels in range.");
        validator.RuleFor(request => request.Mow).Must(mow => mow is null || (ValidLineupUnit(mow) && mow.Rank is null))
            .WithMessage("The lineup MoW needs an id, a known progression, no rank, an XP level and at most two ability levels in range.");
        validator.RuleFor(request => request.Result).IsInEnum().When(request => request.Result is not null);
        validator.RuleFor(request => request.LostUnitIds)
            .Must((request, lost) => lost is null || (request.Result is not null && LostUnitsInLineup(request, lost)))
            .WithMessage("lostUnitIds lists lineup units, each once, and is only sent with a result.");
        validator.RuleFor(request => request.Notes).MaximumLength(GuildWarValidation.MaxNotesLength);
        validator.RuleFor(request => request.Enemy).Must(ValidEnemy!)
            .WithMessage($"Enemy details hold at most {GuildWarValidation.MaxEnemyCharacters} characters, one MoW and {GuildWarValidation.MaxBuffIds} buffs, with valid ids and values.")
            .When(request => request.Enemy is not null);
    }

    private static bool LostUnitsInLineup(IGuildWarAttackFields fields, IReadOnlyList<string> lost)
    {
        if (fields.Characters is null)
            return false;
        var lineup = fields.Characters.Select(unit => unit?.UnitId).Append(fields.Mow?.UnitId).ToHashSet(StringComparer.Ordinal);
        return lost.All(id => id is not null && lineup.Contains(id))
            && lost.Distinct(StringComparer.Ordinal).Count() == lost.Count;
    }

    private static bool ValidId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= GuildWarValidation.MaxIdLength;

    private static bool ValidLineupUnit(GuildWarLineupUnitDto? unit) =>
        unit is not null
        && ValidId(unit.UnitId)
        && Enum.IsDefined(unit.Progression)
        && unit.XpLevel is >= 0 and <= GuildWarValidation.MaxXpLevel
        && unit.AbilityLevels is not null
        && unit.AbilityLevels.Count <= GuildWarValidation.MaxAbilityLevels
        && unit.AbilityLevels.All(level => level is >= 0 and <= GuildWarValidation.MaxAbilityLevel);

    private static bool ValidEnemyUnit(GuildWarEnemyUnitDto? unit) =>
        unit is not null
        && ValidId(unit.UnitId)
        && (unit.Rarity is null || Enum.IsDefined(unit.Rarity.Value))
        && (unit.Rank is null || Enum.IsDefined(unit.Rank.Value))
        && (unit.State is null || Enum.IsDefined(unit.State.Value));

    private static bool ValidEnemy(GuildWarEnemyDto enemy) =>
        enemy.Characters is not null
        && enemy.Characters.Count <= GuildWarValidation.MaxEnemyCharacters
        && enemy.Characters.All(ValidEnemyUnit)
        && (enemy.Mow is null || ValidEnemyUnit(enemy.Mow))
        && enemy.BuffIds is not null
        && enemy.BuffIds.Count <= GuildWarValidation.MaxBuffIds
        && enemy.BuffIds.All(ValidId)
        && (enemy.BoardId is null || ValidId(enemy.BoardId));
}

public sealed class SetCurrentGuildWarValidator : Validator<SetCurrentGuildWarRequest>
{
    public SetCurrentGuildWarValidator()
    {
        GuildWarFieldsValidation.ApplySettings(this, request => request.SeasonId, request => request.BattlefieldLevel);
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }
}

public sealed class StartGuildWarValidator : Validator<StartGuildWarRequest>
{
    public StartGuildWarValidator() =>
        GuildWarFieldsValidation.ApplySettings(this, request => request.SeasonId, request => request.BattlefieldLevel);
}

public sealed class CreateGuildWarAttackValidator : Validator<CreateGuildWarAttackRequest>
{
    public CreateGuildWarAttackValidator()
    {
        GuildWarFieldsValidation.ApplyAttack(this);
        RuleFor(request => request.AttackedAt)
            .Must(attackedAt => attackedAt is null
                || (attackedAt >= GuildWarValidation.MinAttackedAt
                    && attackedAt <= Resolve<TimeProvider>().GetUtcNow() + GuildWarValidation.MaxAttackedAtSkew))
            .WithMessage("attackedAt is from 2020 on and not in the future.");
    }
}

public sealed class UpdateGuildWarAttackValidator : Validator<UpdateGuildWarAttackRequest>
{
    public UpdateGuildWarAttackValidator()
    {
        GuildWarFieldsValidation.ApplyAttack(this);
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }
}
