using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.CurrentUser;

public sealed class UpdateDisplayNameEndpoint : Endpoint<UpdateDisplayNameRequest, UpdateDisplayNameResponse>
{
    public const int MaxLength = 80;

    public override void Configure()
    {
        Put("me/display-name");
        Summary(summary =>
        {
            summary.Summary = "Confirms or edits the current profile's app-owned display name.";
            summary.Description = "Trims the name, requires 1-80 characters without control characters, saves it, "
                + "which confirms it. Names are not required to be unique.";
            summary.Response<UpdateDisplayNameResponse>(StatusCodes.Status200OK, "The saved, confirmed name.");
            summary.Response(StatusCodes.Status400BadRequest, "The name is empty, too long, or has control characters.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    public override async Task HandleAsync(UpdateDisplayNameRequest req, CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId.Value, ct);
        profile.DisplayName = req.DisplayName.Trim();
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new UpdateDisplayNameResponse(profile.DisplayName), ct);
    }
}

public sealed record UpdateDisplayNameRequest(string DisplayName);

public sealed record UpdateDisplayNameResponse(string DisplayName);

public sealed class UpdateDisplayNameValidator : Validator<UpdateDisplayNameRequest>
{
    public UpdateDisplayNameValidator()
    {
        RuleFor(request => request.DisplayName)
            .NotNull()
            .Must(name => name is not null && name.Trim().Length is >= 1 and <= UpdateDisplayNameEndpoint.MaxLength)
            .WithMessage($"The display name must be 1-{UpdateDisplayNameEndpoint.MaxLength} characters.")
            .Must(name => name is not null && !name.Any(char.IsControl))
            .WithMessage("The display name must not contain control characters.");
    }
}
