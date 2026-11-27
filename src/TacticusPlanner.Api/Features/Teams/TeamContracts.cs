using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using TacticusPlanner.Domain.Teams;

namespace TacticusPlanner.Api.Features.Teams;

/// <summary>A team as every teams endpoint returns it (<c>personal-teams</c>). Ids and codes only: the client resolves
/// display names and icons. <c>issues</c> are derived from the current catalog on every read (design D4).</summary>
public sealed record TeamResponse(
    Guid Id,
    long Revision,
    string Name,
    int Position,
    IReadOnlyList<TeamMode> Modes,
    IReadOnlyList<string> CharacterIds,
    int? FlexStartIndex,
    IReadOnlyList<string> MowIds,
    int? MowFlexStartIndex,
    string? CampaignGroupId,
    IReadOnlyList<string> IncursionMowIds,
    string? Notes,
    TeamRarityCap? RarityCap,
    string? SurvivalEventKey,
    IReadOnlyList<TeamRuleCode> Issues
)
{
    public static TeamResponse From(Team team, TeamRuleFacts facts) => new(
        team.Id.Value,
        team.Revision,
        team.Name,
        team.SortOrder,
        team.Modes,
        team.CharacterIds,
        team.FlexStartIndex,
        team.MowIds,
        team.MowFlexStartIndex,
        team.CampaignGroupId,
        team.IncursionMowIds,
        team.Notes,
        team.RarityCap,
        team.SurvivalEventKey,
        TeamRules.Evaluate(team.ToDraft(), facts, team.SurvivalEventKey));
}

public sealed record ListTeamsResponse(IReadOnlyList<TeamResponse> Teams);

/// <summary>The editable fields of a team, shared by create and update.</summary>
public interface ITeamFields
{
    string Name { get; }
    IReadOnlyList<TeamMode> Modes { get; }
    IReadOnlyList<string> CharacterIds { get; }
    int? FlexStartIndex { get; }
    IReadOnlyList<string> MowIds { get; }
    int? MowFlexStartIndex { get; }
    string? CampaignGroupId { get; }
    IReadOnlyList<string> IncursionMowIds { get; }
    string? Notes { get; }
    TeamRarityCap? RarityCap { get; }
    string? SurvivalEventKey { get; }
}

internal static class TeamFieldsExtensions
{
    public static TeamDraft ToDraft(this ITeamFields fields) => new(
        fields.Name, fields.Modes, fields.CharacterIds, fields.FlexStartIndex, fields.MowIds, fields.CampaignGroupId,
        fields.IncursionMowIds, fields.Notes, fields.MowFlexStartIndex, fields.RarityCap,
        fields.SurvivalEventKey);
}

public sealed record CreateTeamRequest(
    string Name,
    IReadOnlyList<TeamMode> Modes,
    IReadOnlyList<string> CharacterIds,
    int? FlexStartIndex,
    IReadOnlyList<string> MowIds,
    int? MowFlexStartIndex,
    string? CampaignGroupId,
    IReadOnlyList<string> IncursionMowIds,
    string? Notes,
    TeamRarityCap? RarityCap,
    string? SurvivalEventKey = null
) : ITeamFields;

/// <summary>
/// Replaces every editable field when <see cref="Revision"/> matches the stored team. <see cref="SurvivalEventKey"/> is
/// the exception: a body without it (a client from before Survival teams) keeps the stored key; an explicit null
/// clears it.
/// </summary>
public sealed record UpdateTeamRequest(
    string Name,
    IReadOnlyList<TeamMode> Modes,
    IReadOnlyList<string> CharacterIds,
    int? FlexStartIndex,
    IReadOnlyList<string> MowIds,
    int? MowFlexStartIndex,
    string? CampaignGroupId,
    IReadOnlyList<string> IncursionMowIds,
    string? Notes,
    TeamRarityCap? RarityCap,
    long Revision
) : ITeamFields
{
    private readonly string? _survivalEventKey;

    /// <summary>The Survival event the team is for; null for none. Absent from the body keeps the stored key.</summary>
    public string? SurvivalEventKey
    {
        get => _survivalEventKey;
        init
        {
            _survivalEventKey = value;
            HasSurvivalEventKey = true;
        }
    }

    /// <summary>Whether the body named <see cref="SurvivalEventKey"/> (System.Text.Json sets only properties present).</summary>
    [JsonIgnore]
    public bool HasSurvivalEventKey { get; private init; }
}

/// <summary>The profile's complete team list in its new order.</summary>
public sealed record UpdateTeamOrderRequest(IReadOnlyList<Guid> TeamIds);

/// <summary>
/// The shape checks every create and update passes before the team rules run: bounded lists, each unit at most once,
/// a flex section that leaves at least one core character and holds at least one flex character, and a MoW flex
/// section that holds at least one flex MoW (every MoW may be flex). Name length is
/// a team rule (<c>nameLength</c>), so it is reported with the other rules, not here.
/// </summary>
internal static class TeamFieldsValidation
{
    // A whole-list check, not RuleForEach: FastEndpoints documents an item rule as the array's maxItems.
    private static bool ValidIds(IReadOnlyList<string> ids) =>
        ids.All(id => !string.IsNullOrEmpty(id) && id.Length <= TeamValidation.MaxUnitIdLength);

    public static void Apply<T>(AbstractValidator<T> validator)
        where T : ITeamFields
    {
        validator.RuleFor(request => request.Name).NotNull().MaximumLength(200);
        validator.RuleFor(request => request.Modes).Cascade(CascadeMode.Stop).NotNull()
            .Must(modes => modes.Distinct().Count() == modes.Count).WithMessage("Each mode can be listed once.");
        validator.RuleFor(request => request.CharacterIds).Cascade(CascadeMode.Stop).NotNull()
            .Must(ids => ids.Count <= TeamValidation.MaxCharacters)
            .WithMessage($"A team can have at most {TeamValidation.MaxCharacters} characters.")
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Each character can be in the lineup once.");
        validator.RuleFor(request => request.CharacterIds).Must(ValidIds).WithMessage($"Unit ids must be 1 to {TeamValidation.MaxUnitIdLength} characters.")
            .When(request => request.CharacterIds is not null);
        validator.RuleFor(request => request.MowIds).Cascade(CascadeMode.Stop).NotNull()
            .Must(ids => ids.Count <= TeamValidation.MaxMows)
            .WithMessage($"A team can have at most {TeamValidation.MaxMows} MoWs.")
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Each MoW can be in the lineup once.");
        validator.RuleFor(request => request.MowIds).Must(ValidIds).WithMessage($"Unit ids must be 1 to {TeamValidation.MaxUnitIdLength} characters.")
            .When(request => request.MowIds is not null);
        validator.RuleFor(request => request.IncursionMowIds).Cascade(CascadeMode.Stop).NotNull()
            .Must(ids => ids.Count <= TeamValidation.MaxIncursionMows)
            .WithMessage($"A team can be built for at most {TeamValidation.MaxIncursionMows} Incursions.")
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Each Incursion can be listed once.");
        validator.RuleFor(request => request.IncursionMowIds).Must(ValidIds).WithMessage($"Unit ids must be 1 to {TeamValidation.MaxUnitIdLength} characters.")
            .When(request => request.IncursionMowIds is not null);
        validator.RuleFor(request => request.FlexStartIndex)
            .Must((request, index) => index is null || (index >= 1 && index <= (request.CharacterIds?.Count ?? 0) - 1))
            .WithMessage("flexStartIndex must leave at least one core and one flex character.");
        validator.RuleFor(request => request.MowFlexStartIndex)
            .Must((request, index) => index is null || (index >= 0 && index <= (request.MowIds?.Count ?? 0) - 1))
            .WithMessage("mowFlexStartIndex must hold at least one flex MoW.");
        validator.RuleFor(request => request.RarityCap).IsInEnum().When(request => request.RarityCap is not null);
        validator.RuleFor(request => request.CampaignGroupId).MaximumLength(TeamValidation.MaxUnitIdLength);
        validator.RuleFor(request => request.SurvivalEventKey).MaximumLength(TeamValidation.MaxUnitIdLength);
        validator.RuleFor(request => request.Notes).MaximumLength(TeamValidation.MaxNotesLength);
    }
}

public sealed class CreateTeamValidator : Validator<CreateTeamRequest>
{
    public CreateTeamValidator() => TeamFieldsValidation.Apply(this);
}

public sealed class UpdateTeamValidator : Validator<UpdateTeamRequest>
{
    public UpdateTeamValidator()
    {
        TeamFieldsValidation.Apply(this);
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateTeamOrderValidator : Validator<UpdateTeamOrderRequest>
{
    public UpdateTeamOrderValidator() =>
        RuleFor(request => request.TeamIds).Cascade(CascadeMode.Stop).NotNull().Must(ids => ids.Count <= 1000);
}
