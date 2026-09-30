using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Goals;

/// <summary>
/// Replaces an in-flight goal's <em>end</em> target in place (<c>edit-goal-targets-in-place</c>): the goal's
/// id, baseline, creation snapshot, status, notes, strategy, memberships and priorities are untouched, the
/// change is checked against an expected revision, and a <see cref="GoalEventType.TargetChanged"/> event
/// records the old and new target. A separate endpoint from <see cref="UpdateGoalEndpoint"/> on purpose:
/// that one's "null clears / null leaves unchanged" field semantics would make an omitted target ambiguous.
/// Everything happens under the same project locks as the other slot mutations, so a Rank target edit and a
/// concurrent membership change can't both claim the same target.
/// </summary>
public sealed class UpdateGoalTargetEndpoint : Endpoint<UpdateGoalTargetRequest, GoalDetailResponse, GoalMapper>
{
    public override void Configure()
    {
        Put("me/goals/{goalId}/target");
        Summary(summary =>
        {
            summary.Summary = "Changes an active or paused goal's end target in place.";
            summary.Description = "Supported for Rank, Ascension, Ability and Upgrade goals; Unlock has no "
                + "adjustable target. The new target is validated against the goal's stored baseline and the "
                + "current catalog (an already-reached target is allowed). Submitting the target the goal "
                + "already has is a no-op that changes neither the revision nor the history.";
            summary.Response<GoalDetailResponse>(StatusCodes.Status200OK, "The updated (or unchanged) goal.");
            summary.Response(StatusCodes.Status400BadRequest,
                "The target is malformed, invalid for this goal, or the goal is Unlock/Completed/Archived.");
            summary.Response<GoalRevisionConflictResponse>(StatusCodes.Status409Conflict,
                "expectedRevision is stale (issueCode goalRevisionStale, carrying the current goal), or a "
                + "Rank target is already held in another project (a ProjectGoalSlotConflictResponse).");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No matching goal owned by the caller.");
        });
    }

    public override async Task HandleAsync(UpdateGoalTargetRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var goalId = GoalId.From(Route<Guid>("goalId"));
        var db = Resolve<PlannerDbContext>();
        var planning = Resolve<ProjectGoalPlanningService>();
        var editor = Resolve<GoalTargetEditor>();

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var goal = await db.Goals.FirstOrDefaultAsync(entity => entity.Id == goalId, ct);
        if (goal is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (GoalTargetEditor.RejectionFor(goal, req.Target) is { } rejection)
        {
            AddError(request => request.Target, rejection);
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var lockedProjectIds = await db.ProjectGoals
            .Where(entry => entry.GoalId == goal.Id)
            .Select(entry => entry.ProjectId)
            .ToListAsync(ct);

        // Same restart-on-membership-drift loop as UpdateGoalStatusEndpoint: every project this decision
        // touches must be locked before it is read (ProjectGoalPlanningService's isolation-level invariant).
        while (true)
        {
            List<ProjectId>? restartWithProjectIds = null;
            await planning.ExecuteLockedMutationAsync(lockedProjectIds, async transaction =>
            {
                // Loaded before the lock; a status/target/membership change that committed while this request
                // waited would otherwise be invisible.
                // A full re-read, not ReloadAsync: `before`, the no-op check and the stale-409 body all read
                // Config/Events, which ReloadAsync leaves at their pre-lock values (GoalQueries.ReloadGoalAsync).
                var reloaded = await db.ReloadGoalAsync(goal, ct);
                if (reloaded is null)
                {
                    await Send.NotFoundAsync(ct);
                    return;
                }

                goal = reloaded;
                var membershipProjectIds = await db.ProjectGoals
                    .Where(entry => entry.GoalId == goal.Id)
                    .Select(entry => entry.ProjectId)
                    .ToListAsync(ct);
                if (membershipProjectIds.Except(lockedProjectIds).Any())
                {
                    restartWithProjectIds = membershipProjectIds.Union(lockedProjectIds).ToList();
                    return;
                }

                var result = await editor.ApplyAsync(
                    profileId, goal, membershipProjectIds, req.Target, req.ExpectedRevision, ct);
                if (result is GoalEditResult.Applied { Changed: true })
                {
                    result = await GoalEditSaver.SaveAsync(
                        db, planning, transaction, goalId,
                        new ProjectGoalSlotLookup(
                            membershipProjectIds, goal.EntityType, goal.EntityId, goal.GoalType,
                            RankTargetKey.For(goal.GoalType, goal.Config), goal.Id),
                        membershipProjectIds, ct);
                    if (result is GoalEditResult.Applied && transaction is not null)
                        await transaction.CommitAsync(ct);
                }

                switch (result)
                {
                    case GoalEditResult.Applied:
                        await Send.OkAsync(Map.ToDetail(goal, membershipProjectIds.Select(id => id.Value).ToList()), ct);
                        break;
                    case GoalEditResult.Invalid invalid:
                        AddError(request => request.Target, invalid.Message);
                        await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                        break;
                    case GoalEditResult.StaleRevision stale:
                        await SendStaleAsync(stale.Current, stale.ProjectIds, ct);
                        break;
                    case GoalEditResult.SlotConflict slotConflict:
                        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                        await HttpContext.Response.WriteAsJsonAsync(slotConflict.Body, ct);
                        break;
                    default:
                        await Send.NotFoundAsync(ct);
                        break;
                }
            }, ct);

            if (restartWithProjectIds is null)
                break;
            lockedProjectIds = restartWithProjectIds;
        }
    }

    private async Task SendStaleAsync(Goal current, List<ProjectId> projectIds, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await HttpContext.Response.WriteAsJsonAsync(
            new GoalRevisionConflictResponse(
                "goalRevisionStale",
                "The goal changed since it was loaded. Review the current target and try again.",
                Map.ToDetail(current, projectIds.Select(id => id.Value).ToList())),
            ct);
    }
}

/// <param name="ExpectedRevision">The <c>revision</c> of the goal detail the editor started from.</param>
public sealed record UpdateGoalTargetRequest(long ExpectedRevision, GoalTargetEditRequest Target);

/// <summary>Exactly the group matching the goal's kind must be set; every other group must be null.
/// Only end values are editable — the start/baseline stays as stored.</summary>
public sealed record GoalTargetEditRequest(
    RankEndTargetRequest? Rank = null,
    ProgressionEndTargetRequest? Progression = null,
    AbilityEndTargetRequest? Ability = null,
    UpgradeTargetRequest? Upgrade = null
);

public sealed record RankEndTargetRequest(int End, bool EndPointFive, int EndAppliedUpgrades);

public sealed record ProgressionEndTargetRequest(string End);

public sealed record AbilityEndTargetRequest(int ActiveEnd, int PassiveEnd);

/// <summary>409 body for a stale <c>expectedRevision</c>: the current goal, so the client can show what
/// changed without another round trip. (A Rank target collision returns a
/// <see cref="ProjectGoalSlotConflictResponse"/> instead — distinguish by <c>issueCode</c>.)</summary>
public sealed record GoalRevisionConflictResponse(string IssueCode, string Message, GoalDetailResponse Goal);

public sealed class UpdateGoalTargetValidator : Validator<UpdateGoalTargetRequest>
{
    public UpdateGoalTargetValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Target).NotNull();

        // Creation returns a 400 for a missing upgrade list / blank ids; the edit must too, before the
        // handler dereferences them.
        When(request => request.Target?.Upgrade is not null, () =>
        {
            RuleFor(request => request.Target.Upgrade!.Targets)
                .NotNull()
                .WithMessage("An upgrade target needs a list of materials.");
            RuleForEach(request => request.Target.Upgrade!.Targets)
                .Must(target => target is not null && !string.IsNullOrWhiteSpace(target.UpgradeId))
                .When(request => request.Target.Upgrade!.Targets is not null)
                .WithMessage("Every upgrade target needs an upgrade id.");
        });

        When(request => request.Target?.Progression is not null, () =>
            RuleFor(request => request.Target.Progression!.End)
                .NotEmpty()
                .WithMessage("An Ascension target needs a progression step."));
    }
}
