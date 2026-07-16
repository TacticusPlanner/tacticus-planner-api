using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;
using PlanningSettingsEntity = TacticusPlanner.Domain.Planning.PlanningSettings;

namespace TacticusPlanner.Api.Features.PlanningSettings;

public sealed class GetPlanningSettingsEndpoint : EndpointWithoutRequest<PlanningSettingsResponse>
{
    public override void Configure()
    {
        Get("me/planning-settings");
        Summary(summary => summary.Summary = "Gets the current profile's Goals planning settings.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var settings = await db.PlanningSettings.FirstOrDefaultAsync(entity => entity.Id == profileId.Value, ct);
        if (settings is null)
        {
            settings = new PlanningSettingsEntity { Id = profileId.Value };
            db.PlanningSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(PlanningSettingsResponse.From(settings), ct);
    }
}

public sealed record PlanningSettingsResponse(int DailyEnergy, long Revision)
{
    public static PlanningSettingsResponse From(PlanningSettingsEntity settings) =>
        new(settings.DailyEnergy, settings.Revision);
}
