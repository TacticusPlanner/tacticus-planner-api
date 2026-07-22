using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Updates a goal's editable fields only (plan §7). The target end-state in <see cref="Goal.Config"/> and
/// the creation snapshot are immutable — redefining them means creating a replacement goal, not editing
/// this one. Only <c>notes</c> and the farming-location override are writable here.
/// </summary>
public sealed class UpdateGoalEndpoint : Endpoint<UpdateGoalRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}");
        Summary(summary =>
        {
            summary.Summary = "Updates a goal's editable fields (notes, farming-location override).";
            summary.Description = "The goal's target end-state and creation snapshot cannot be changed here "
                + "— create a replacement goal instead.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated goal.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var goalId = Route<Guid>("goalId");
        var db = Resolve<PlannerDbContext>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == GoalId.From(goalId), ct);

        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        goal.Notes = req.Notes;
        goal.Config.FarmingLocationIds = req.FarmingLocationIds?.Select(id => id.Value).ToList();
        if (req.FarmingStrategy is not null)
        {
            goal.Config.FarmingStrategy = Enum.Parse<FarmingStrategy>(req.FarmingStrategy, ignoreCase: true);
        }

        await db.SaveChangesAsync(ct);

        var projectIds = await db.ProjectIdsAsync(goal.Id, ct);
        await Send.OkAsync(Map.ToDetail(goal, projectIds), ct);
    }
}

public sealed record UpdateGoalRequest(
    string? Notes,
    List<CampaignBattleId>? FarmingLocationIds,
    string? FarmingStrategy = null
);

public sealed class UpdateGoalValidator : Validator<UpdateGoalRequest>
{
    public UpdateGoalValidator()
    {
        RuleFor(request => request.FarmingStrategy)
            .Must(value => value is null || Enum.TryParse<FarmingStrategy>(value, ignoreCase: true, out _))
            .WithMessage("Unknown farming strategy.");
    }
}
