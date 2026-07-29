using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;
using UserSettingsData = TacticusPlanner.Domain.UserSettings.UserSettingsData;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Api.Features.UserSettings;

public sealed class UpdateUserSettingsEndpoint
    : Endpoint<UpdateUserSettingsRequest, UserSettingsResponse>
{
    public override void Configure()
    {
        Put("me/user-settings");
        Summary(summary => summary.Summary = "Updates the current profile's user settings.");
    }

    public override async Task HandleAsync(UpdateUserSettingsRequest req, CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var settings = await db.UserSettings.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct);
        if (settings is null)
        {
            if (req.Revision != 0)
            {
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
            }

            settings = new UserSettingsEntity { Id = profileIdValue };
            db.UserSettings.Add(settings);
        }
        else if (settings.Revision != req.Revision)
        {
            AddError("User settings changed on another device. Refresh and try again.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        settings.Settings = new UserSettingsData { DailyEnergy = req.DailyEnergy };

        // A nested owned-JSON property mutation isn't always picked up by snapshot change detection on
        // its owning entity — force Modified explicitly so EntityMetadataInterceptor reliably bumps
        // Revision/UpdatedAt (and so an unchanged-value PUT still counts as a real update).
        if (db.Entry(settings).State == EntityState.Unchanged)
        {
            db.Entry(settings).State = EntityState.Modified;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            AddError("User settings changed on another device. Refresh and try again.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.OkAsync(UserSettingsResponse.From(settings), ct);
    }
}

public sealed record UpdateUserSettingsRequest(int DailyEnergy, long Revision);

public sealed class UpdateUserSettingsValidator : Validator<UpdateUserSettingsRequest>
{
    public UpdateUserSettingsValidator()
    {
        RuleFor(request => request.DailyEnergy)
            .Must(UserSettingsData.SupportedDailyEnergy.Contains)
            .WithMessage("Daily energy must be one of the supported planning tiers.");
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }
}
