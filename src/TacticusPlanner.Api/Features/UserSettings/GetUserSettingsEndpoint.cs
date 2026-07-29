using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Api.Features.UserSettings;

public sealed class GetUserSettingsEndpoint : EndpointWithoutRequest<UserSettingsResponse>
{
    public override void Configure()
    {
        Get("me/user-settings");
        Summary(summary => summary.Summary = "Gets the current profile's user settings.");
    }

    public override async Task HandleAsync(CancellationToken ct)
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
            settings = new UserSettingsEntity { Id = profileIdValue };
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(UserSettingsResponse.From(settings), ct);
    }
}

public sealed record UserSettingsResponse(int DailyEnergy, long Revision)
{
    public static UserSettingsResponse From(UserSettingsEntity settings) =>
        new(settings.Settings.DailyEnergy, settings.Revision);
}
