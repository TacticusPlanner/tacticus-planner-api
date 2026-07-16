using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;
using PlanningSettingsEntity = TacticusPlanner.Domain.Planning.PlanningSettings;

namespace TacticusPlanner.Api.Features.PlanningSettings;

public sealed class UpdatePlanningSettingsEndpoint
    : Endpoint<UpdatePlanningSettingsRequest, PlanningSettingsResponse>
{
    public override void Configure()
    {
        Put("me/planning-settings");
        Summary(summary => summary.Summary = "Updates the current profile's Goals planning settings.");
    }

    public override async Task HandleAsync(UpdatePlanningSettingsRequest req, CancellationToken ct)
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
            if (req.Revision != 0)
            {
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
            }

            settings = new PlanningSettingsEntity { Id = profileId.Value };
            db.PlanningSettings.Add(settings);
        }
        else if (settings.Revision != req.Revision)
        {
            AddError("Planning settings changed on another device. Refresh and try again.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        settings.DailyEnergy = req.DailyEnergy;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            AddError("Planning settings changed on another device. Refresh and try again.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.OkAsync(PlanningSettingsResponse.From(settings), ct);
    }
}

public sealed record UpdatePlanningSettingsRequest(int DailyEnergy, long Revision);

public sealed class UpdatePlanningSettingsValidator : Validator<UpdatePlanningSettingsRequest>
{
    public UpdatePlanningSettingsValidator()
    {
        RuleFor(request => request.DailyEnergy)
            .Must(PlanningSettingsEntity.SupportedDailyEnergy.Contains)
            .WithMessage("Daily energy must be one of the supported planning tiers.");
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }
}
