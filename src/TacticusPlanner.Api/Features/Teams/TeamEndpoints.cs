using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Teams;

/// <summary>Responses shared by the teams endpoints.</summary>
internal static class TeamProblems
{
    public static TeamId? RouteId(IEndpoint endpoint) =>
        Guid.TryParse(endpoint.HttpContext.GetRouteValue("teamId")?.ToString(), out var guid) ? TeamId.From(guid) : null;

    /// <summary>400 <c>teamRulesBroken</c> naming each broken rule under <c>rules</c>, in rules-table order.</summary>
    public static Task SendRulesBrokenAsync(IEndpoint endpoint, IReadOnlyList<TeamRuleCode> rules, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const string message = "The team breaks one or more team rules.";
        return ApiProblems.WriteAsync(
            endpoint.HttpContext, StatusCodes.Status400BadRequest, TeamsService.RulesBroken, message,
            extensions: new Dictionary<string, object?>
            {
                ["rules"] = rules,
                ["errors"] = new Dictionary<string, string[]> { ["rules"] = [message] },
            });
    }

    public static ValidationFailure NameTakenFailure() =>
        new("name", "Another team already has this name.") { ErrorCode = TeamsService.NameTaken };

    public static ValidationFailure LimitFailure(int limit) =>
        new("general", $"A profile can have at most {limit} teams; delete one to make room.")
        {
            ErrorCode = TeamsService.LimitReached,
        };
}

public sealed class ListTeamsEndpoint : EndpointWithoutRequest<ListTeamsResponse>
{
    public override void Configure()
    {
        Get("teams");
        Summary(summary =>
        {
            summary.Summary = "Lists the caller's teams in their saved order.";
            summary.Description = "issues are the team rules each team breaks against the current catalog, derived on "
                + "every read; a team with issues stays readable, reorderable and deletable.";
            summary.Response<ListTeamsResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        await Send.OkAsync(await Resolve<TeamsService>().ListResponseAsync(ct), ct);
    }
}

public sealed class CreateTeamEndpoint : Endpoint<CreateTeamRequest, TeamResponse>
{
    public override void Configure()
    {
        Post("teams");
        Summary(summary =>
        {
            summary.Summary = "Creates a team at the end of the order.";
            summary.Response<TeamResponse>(StatusCodes.Status200OK, "The created team, as GET returns it.");
            summary.Response(StatusCodes.Status400BadRequest,
                "teamRulesBroken (each broken rule under rules), teamNameTaken, teamLimitReached, or an invalid shape.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
        });
    }

    public override async Task HandleAsync(CreateTeamRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<TeamsService>();
        var facts = service.Facts;
        var draft = req.ToDraft();
        var broken = TeamRules.Evaluate(draft, facts);
        if (broken.Count > 0)
        {
            await TeamProblems.SendRulesBrokenAsync(this, broken, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var created = await service.ExecuteLockedAsync<Team?>(async transaction =>
        {
            var count = await db.Teams.CountAsync(ct);
            if (count >= service.Limit)
            {
                AddError(TeamProblems.LimitFailure(service.Limit));
                return null;
            }

            if (await service.IsNameTakenAsync(draft.Name, null, ct))
            {
                AddError(TeamProblems.NameTakenFailure());
                return null;
            }

            var team = Team.Create(profileId, draft, count + 1);
            db.Teams.Add(team);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (TeamsService.IsNameConflict(exception))
            {
                AddError(TeamProblems.NameTakenFailure());
                return null;
            }

            await transaction.CommitAsync(ct);
            return team;
        }, ct);

        if (created is null)
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await Send.OkAsync(TeamResponse.From(created, facts), ct);
    }
}

public sealed class UpdateTeamEndpoint : Endpoint<UpdateTeamRequest, TeamResponse>
{
    public override void Configure()
    {
        Put("teams/{teamId}");
        Summary(summary =>
        {
            summary.Summary = "Replaces a team's fields when the revision matches.";
            summary.Description = "The updated team must satisfy every team rule, including a team that was imported "
                + "with issues. The id, position and V1 import key never change.";
            summary.Response<TeamResponse>(StatusCodes.Status200OK, "The updated team, as GET returns it.");
            summary.Response(StatusCodes.Status400BadRequest,
                "teamRulesBroken (each broken rule under rules), teamNameTaken, or an invalid shape.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The team does not exist or belongs to another profile.");
            summary.Response(StatusCodes.Status409Conflict, "teamRevisionStale; current is the team as it stands.");
        });
    }

    public override async Task HandleAsync(UpdateTeamRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null || TeamProblems.RouteId(this) is not { } teamId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<TeamsService>();
        var facts = service.Facts;
        IReadOnlyList<TeamRuleCode> broken = [];

        var db = Resolve<PlannerDbContext>();
        // Under the profile lock, like create, delete, reorder and the V1 import: a rename cannot take a name the
        // import is about to insert, and no delete can move this team's position before the response reads it.
        var (outcome, result) = await service.ExecuteLockedAsync<(UpdateOutcome, Team?)>(async transaction =>
        {
            // Scoped to the caller's profile by PlannerDbContext's global query filter.
            var team = await db.Teams.FirstOrDefaultAsync(entity => entity.Id == teamId, ct);
            if (team is null)
                return (UpdateOutcome.NotFound, null);
            if (team.Revision != req.Revision)
                return (UpdateOutcome.Stale, team);
            // A body without the Survival key (a client from before Survival teams) keeps the stored one.
            var draft = req.HasSurvivalEventKey ? req.ToDraft() : req.ToDraft() with { SurvivalEventKey = team.SurvivalEventKey };
            broken = TeamRules.Evaluate(draft, facts, team.SurvivalEventKey);
            if (broken.Count > 0)
                return (UpdateOutcome.RulesBroken, null);
            if (await service.IsNameTakenAsync(draft.Name, teamId, ct))
                return (UpdateOutcome.NameTaken, null);

            team.Apply(draft);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                var current = await db.Teams.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == teamId, ct);
                return current is null ? (UpdateOutcome.NotFound, null) : (UpdateOutcome.Stale, current);
            }
            catch (DbUpdateException exception) when (TeamsService.IsNameConflict(exception))
            {
                return (UpdateOutcome.NameTaken, null);
            }

            await transaction.CommitAsync(ct);
            return (UpdateOutcome.Updated, team);
        }, ct);

        switch (outcome)
        {
            case UpdateOutcome.NotFound:
                await this.SendNotFoundProblemAsync(ct);
                return;
            case UpdateOutcome.Stale:
                await SendStaleAsync(result!, facts, ct);
                return;
            case UpdateOutcome.RulesBroken:
                await TeamProblems.SendRulesBrokenAsync(this, broken, ct);
                return;
            case UpdateOutcome.NameTaken:
                AddError(TeamProblems.NameTakenFailure());
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            default:
                await Send.OkAsync(TeamResponse.From(result!, facts), ct);
                return;
        }
    }

    private enum UpdateOutcome
    {
        Updated,
        NotFound,
        Stale,
        RulesBroken,
        NameTaken,
    }

    private Task SendStaleAsync(Team current, TeamRuleFacts facts, CancellationToken ct) =>
        this.SendConflictAsync(
            TeamsService.RevisionStale, "The team changed on another device. Refresh and try again.",
            TeamResponse.From(current, facts), ct);
}

public sealed class DeleteTeamEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("teams/{teamId}");
        Summary(summary =>
        {
            summary.Summary = "Deletes a team and closes the gap in the remaining positions.";
            summary.Response(StatusCodes.Status204NoContent, "The team was deleted.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The team does not exist or belongs to another profile.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId || TeamProblems.RouteId(this) is not { } teamId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var service = Resolve<TeamsService>();
        var deleted = await service.ExecuteLockedAsync(async transaction =>
        {
            var removed = await db.Teams.Where(entity => entity.Id == teamId).ExecuteDeleteAsync(ct);
            if (removed == 0)
                return false;
            var remaining = await db.Teams.AsNoTracking()
                .OrderBy(team => team.SortOrder).ThenBy(team => team.Id)
                .Select(team => team.Id)
                .ToListAsync(ct);
            await service.WritePositionsAsync(profileId, remaining, ct);
            await transaction.CommitAsync(ct);
            return true;
        }, ct);

        if (!deleted)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}

public sealed class UpdateTeamOrderEndpoint : Endpoint<UpdateTeamOrderRequest, ListTeamsResponse>
{
    public override void Configure()
    {
        Put("teams/order");
        Summary(summary =>
        {
            summary.Summary = "Reorders the caller's teams as a whole list.";
            summary.Description = "teamIds must be exactly the profile's current teams, in their new order. A reorder "
                + "changes no other field of any team, including its revision.";
            summary.Response<ListTeamsResponse>(StatusCodes.Status200OK, "The reordered teams.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
            summary.Response(StatusCodes.Status409Conflict,
                "teamOrderStale: teamIds is not the profile's current set of teams; current is the current list.");
        });
    }

    public override async Task HandleAsync(UpdateTeamOrderRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var service = Resolve<TeamsService>();
        var applied = await service.ExecuteLockedAsync(async transaction =>
        {
            var current = await db.Teams.AsNoTracking().Select(team => team.Id.Value).ToListAsync(ct);
            var requested = req.TeamIds;
            if (requested.Count != current.Count
                || requested.Distinct().Count() != requested.Count
                || !requested.ToHashSet().SetEquals(current))
                return false;

            await service.WritePositionsAsync(profileId, [.. requested.Select(TeamId.From)], ct);
            await transaction.CommitAsync(ct);
            return true;
        }, ct);

        var list = await service.ListResponseAsync(ct);
        if (!applied)
        {
            await this.SendConflictAsync(
                TeamsService.OrderStale, "Your teams changed on another device. Refresh and try again.", list, ct);
            return;
        }

        await Send.OkAsync(list, ct);
    }
}
