using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using UserSettingsData = TacticusPlanner.Domain.UserSettings.UserSettingsData;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Api.Features.UserSettings;

public sealed class UpdateUserSettingsEndpoint
    : Endpoint<UpdateUserSettingsRequest, UserSettingsResponse>
{
    public override void Configure()
    {
        Put("settings");
        Summary(summary => summary.Summary = "Updates the current profile's user settings.");
    }

    public override async Task HandleAsync(UpdateUserSettingsRequest req, CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var settings = await db.UserSettings.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct);
        if (settings is null)
        {
            if (req.Revision != 0)
            {
                await SendConflictAsync(db, profileIdValue, ct);
                return;
            }

            settings = new UserSettingsEntity { Id = profileIdValue };
            db.UserSettings.Add(settings);
        }
        else if (settings.Revision != req.Revision)
        {
            await SendConflictAsync(db, profileIdValue, ct);
            return;
        }

        settings.Settings = new UserSettingsData { DailyEnergy = req.DailyEnergy, XpBookRarity = req.XpBookRarity.ToString() };

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
            await SendConflictAsync(db, profileIdValue, ct);
            return;
        }

        await Send.OkAsync(UserSettingsResponse.From(settings), ct);
    }

    /// <summary>409 <c>concurrency</c> with the settings re-read after the failed write, in the read shape (a
    /// missing row reads as the defaults at revision 0).</summary>
    private async Task SendConflictAsync(PlannerDbContext db, ProfileId profileId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var current = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == profileId, ct)
            ?? new UserSettingsEntity { Id = profileId };
        await this.SendConflictAsync(
            ApiProblems.Concurrency, "User settings changed on another device. Refresh and try again.",
            UserSettingsResponse.From(current), ct);
    }
}

public sealed record UpdateUserSettingsRequest(
    int DailyEnergy,
    [property: JsonRequired] XpBookRarity XpBookRarity,
    long Revision);

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
