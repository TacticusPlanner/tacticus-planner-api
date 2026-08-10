using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Projects;

public sealed class UpdateProjectUnitOrderEndpoint : Endpoint<UpdateProjectUnitOrderRequest, ProjectGoalsResponse>
{
    public override void Configure()
    {
        Put("me/projects/{projectId}/unit-order");
        Summary(summary =>
        {
            summary.Summary = "Reorders all Character and Machine of War blocks in a project.";
            summary.Response<ProjectGoalsResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status400BadRequest, "The request is not an exact permutation of the project's units.");
            summary.Response(StatusCodes.Status404NotFound);
        });
    }

    public override async Task HandleAsync(UpdateProjectUnitOrderRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var projectId = ProjectId.From(Route<Guid>("projectId"));
        if (!await db.Projects.AnyAsync(project => project.Id == projectId, ct))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var planning = Resolve<ProjectGoalPlanningService>();
        await planning.ExecuteLockedMutationAsync([projectId], async transaction =>
        {
            if (!await planning.ApplyUnitOrderAsync(projectId, req.Units, ct))
            {
                AddError(request => request.Units, "Units must be an exact, duplicate-free permutation of the project's current units.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            var goals = await db.ProjectGoals.AsNoTracking()
                .Where(entry => entry.ProjectId == projectId)
                .OrderBy(entry => entry.Priority)
                .Select(entry => new ProjectGoalEntryResponse(entry.GoalId.Value, entry.Priority))
                .ToListAsync(ct);
            await Send.OkAsync(new ProjectGoalsResponse(goals), ct);
        }, ct);
    }
}

public sealed record UpdateProjectUnitOrderRequest(List<UnitOrderEntryRequest> Units);

public sealed class UpdateProjectUnitOrderValidator : Validator<UpdateProjectUnitOrderRequest>
{
    public UpdateProjectUnitOrderValidator()
    {
        RuleFor(request => request.Units).NotNull();
        RuleForEach(request => request.Units).ChildRules(unit =>
        {
            unit.RuleFor(value => value.EntityType)
                .Must(value => Enum.TryParse<GoalEntityType>(value, true, out var parsed) && Enum.IsDefined(parsed))
                .WithMessage("Entity type must be Character or Mow.");
            unit.RuleFor(value => value.EntityId).NotEmpty();
        });
    }
}
