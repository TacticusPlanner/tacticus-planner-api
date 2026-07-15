using FastEndpoints;
using FluentValidation;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// The username/password-required check is a pure request-shape rule (unlike the handler's later checks,
/// which depend on calling the V1 API/Tacticus key validator), so it belongs in a validator rather than an
/// inline handler branch.
/// </summary>
public sealed class ImportV1ProfileValidator : Validator<ImportV1ProfileRequest>
{
    public ImportV1ProfileValidator()
    {
        RuleFor(request => request.Username)
            .NotEmpty()
            .WithMessage("The V1 username and password are required.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .WithMessage("The V1 username and password are required.");

        RuleFor(request => request.Import)
            .NotNull()
            .Must(selection => selection is not null
                && (selection.PersonalTacticusApiKey || selection.TacticusUserId || selection.GuildApiToken || selection.Goals))
            .WithMessage("Select at least one V1 profile part to import.");
    }
}
