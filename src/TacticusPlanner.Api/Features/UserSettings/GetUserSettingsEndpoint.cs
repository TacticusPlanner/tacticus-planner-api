using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;
using UserSettingsData = TacticusPlanner.Domain.UserSettings.UserSettingsData;
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

public sealed record UserSettingsResponse(int DailyEnergy, string XpBookRarity, long Revision)
{
    public static UserSettingsResponse From(UserSettingsEntity settings) =>
        new(settings.Settings.DailyEnergy, NormalizeXpBookRarity(settings.Settings.XpBookRarity), settings.Revision);

    // A row saved before this field existed (or any other missing/null/unsupported stored value) reads
    // as the default rather than leaking an invalid value to the client.
    private static string NormalizeXpBookRarity(string? xpBookRarity) =>
        xpBookRarity is not null && UserSettingsData.SupportedXpBookRarity.Contains(xpBookRarity)
            ? xpBookRarity
            : UserSettingsData.DefaultXpBookRarity;
}
