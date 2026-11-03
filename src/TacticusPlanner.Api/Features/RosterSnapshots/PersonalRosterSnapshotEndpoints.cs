using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Features.UpstreamRateLimiting;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.RosterSnapshots;

internal static class PersonalRosterSnapshotProblems
{
    public const string LimitReached = "rosterSnapshotLimitReached";
    public const string PlayerDataNotSynced = "playerDataNotSynced";
    public const string NotAutomatic = "rosterSnapshotNotAutomatic";

    public static bool IsFromLastSync(HttpRequest request) =>
        bool.TryParse(request.Query["fromLastSync"].ToString(), out var value) && value;

    public static Task SendLimitReachedAsync(IEndpoint endpoint, int limit, int count, CancellationToken ct) =>
        endpoint.SendConflictAsync(
            LimitReached,
            "Delete a snapshot to take a new one.",
            current: null,
            ct,
            new Dictionary<string, object?> { ["limit"] = limit, ["count"] = count });

    /// <summary>The route's snapshot id, or null when it is not a uuid (answered as 404).</summary>
    public static PersonalRosterSnapshotId? RouteId(IEndpoint endpoint) =>
        Guid.TryParse(endpoint.HttpContext.GetRouteValue("snapshotId")?.ToString(), out var guid)
            ? PersonalRosterSnapshotId.From(guid)
            : null;
}

public sealed class CreatePersonalRosterSnapshotEndpoint
    : Endpoint<CreatePersonalRosterSnapshotRequest, PersonalRosterSnapshotResponse>
{
    public override void Configure()
    {
        Post("me/roster-snapshots");
        Options(builder => builder.WithMetadata(
            UpstreamRateLimit.SlowUnless(PersonalRosterSnapshotProblems.IsFromLastSync)));
        Summary(summary =>
        {
            summary.Summary = "Takes a personal roster snapshot from a fresh sync.";
            summary.Description = "Syncs player data from the Tacticus API and, in the same transaction, stores a copy "
                + "of the whole synced roster as a snapshot with source manual. With fromLastSync=true it copies the "
                + "last synced roster without calling the Tacticus API (takenAt is that sync's time) and is not rate "
                + "limited. A sync failure creates nothing. At the limit (manual and V1-imported snapshots) the request "
                + "is rejected before any sync with 409 rosterSnapshotLimitReached, carrying limit and count.";
            summary.Response<PersonalRosterSnapshotResponse>(StatusCodes.Status200OK, "The created snapshot.");
            summary.Response(StatusCodes.Status400BadRequest,
                "The name is too long, no Tacticus API key is configured, or the Tacticus API rejected it.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
            summary.Response(StatusCodes.Status409Conflict,
                "rosterSnapshotLimitReached (with limit and count), or playerDataNotSynced for fromLastSync=true.");
            summary.Response(StatusCodes.Status429TooManyRequests, UpstreamRateLimit.ResponseDescription);
            summary.Response(StatusCodes.Status503ServiceUnavailable, "The Tacticus API failed or timed out (upstreamUnavailable).");
        });
    }

    public override async Task HandleAsync(CreatePersonalRosterSnapshotRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var result = await Resolve<PersonalRosterSnapshotService>()
            .CreateAsync(profileId, req.Name, req.FromLastSync == true, ct);
        switch (result)
        {
            case CreatePersonalRosterSnapshotResult.Created created:
                await Send.OkAsync(PersonalRosterSnapshotResponse.From(created.Snapshot), ct);
                return;
            case CreatePersonalRosterSnapshotResult.LimitReached limit:
                await PersonalRosterSnapshotProblems.SendLimitReachedAsync(this, limit.Limit, limit.Count, ct);
                return;
            case CreatePersonalRosterSnapshotResult.NotSynced:
                await this.SendConflictAsync(
                    PersonalRosterSnapshotProblems.PlayerDataNotSynced,
                    "Sync your player data before taking a snapshot from the last sync.",
                    current: null,
                    ct);
                return;
            case CreatePersonalRosterSnapshotResult.UpstreamUnavailable:
                await this.SendProblemAsync(
                    StatusCodes.Status503ServiceUnavailable, ct, "The Tacticus API is currently unavailable. Try again shortly.");
                return;
            case CreatePersonalRosterSnapshotResult.NoApiKey:
                AddError("No Tacticus API key is configured for this profile.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            default:
                AddError("The Tacticus API could not fetch player data for the configured key.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
        }
    }
}

public sealed class ListPersonalRosterSnapshotsEndpoint : EndpointWithoutRequest<PersonalRosterSnapshotListResponse>
{
    public override void Configure()
    {
        Get("me/roster-snapshots");
        Summary(summary =>
        {
            summary.Summary = "Lists the caller's personal roster snapshots, newest first, without rosters.";
            summary.Description = "count is the number that counts toward limit (manual and V1-imported snapshots); "
                + "autoCount the automatic snapshots, kept to autoLimit by replacing the oldest. "
                + "Legendary Event run-record rosters are not listed here.";
            summary.Response<PersonalRosterSnapshotListResponse>(StatusCodes.Status200OK);
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

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var items = await Resolve<PlannerDbContext>().PersonalRosterSnapshots.AsNoTracking()
            .OrderByDescending(entity => entity.TakenAt)
            .ThenByDescending(entity => entity.Id)
            .Select(entity => new PersonalRosterSnapshotResponse(
                entity.Id.Value, entity.Name, entity.Source, entity.TakenAt, entity.CharacterCount, entity.MowCount))
            .ToListAsync(ct);
        var service = Resolve<PersonalRosterSnapshotService>();
        await Send.OkAsync(
            new PersonalRosterSnapshotListResponse(
                service.Limit,
                items.Count(item => PersonalRosterSnapshotValidation.CountsTowardLimit(item.Source)),
                service.AutoLimit,
                items.Count(item => item.Source == RosterSnapshotSource.Auto),
                items),
            ct);
    }
}

public sealed class GetPersonalRosterSnapshotEndpoint : EndpointWithoutRequest<RosterSnapshotResponse>
{
    public override void Configure()
    {
        Get("me/roster-snapshots/{snapshotId}");
        Summary(summary =>
        {
            summary.Summary = "Reads the roster of one of the caller's personal snapshots.";
            summary.Description = "The roster never changes after creation, so the response may be cached for good.";
            summary.Response<RosterSnapshotResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No snapshot with this id belongs to the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || PersonalRosterSnapshotProblems.RouteId(this) is not { } snapshotId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var roster = await Resolve<PlannerDbContext>().PersonalRosterSnapshots.AsNoTracking()
            .Where(entity => entity.Id == snapshotId)
            .Select(entity => entity.Roster)
            .FirstOrDefaultAsync(ct);
        if (roster is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        HttpContext.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        await Send.OkAsync(RosterSnapshotResponse.From(roster), ct);
    }
}

public sealed class RenamePersonalRosterSnapshotEndpoint
    : Endpoint<RenamePersonalRosterSnapshotRequest, PersonalRosterSnapshotResponse>
{
    public override void Configure()
    {
        Patch("me/roster-snapshots/{snapshotId}");
        Summary(summary =>
        {
            summary.Summary = "Renames one of the caller's personal snapshots.";
            summary.Description = "The name is trimmed and must be 1–60 characters; it need not be unique. The roster "
                + "and takenAt do not change.";
            summary.Response<PersonalRosterSnapshotResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status400BadRequest, "The name is blank or longer than 60 characters.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No snapshot with this id belongs to the caller.");
        });
    }

    public override async Task HandleAsync(RenamePersonalRosterSnapshotRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || PersonalRosterSnapshotProblems.RouteId(this) is not { } snapshotId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter. The metadata alone is updated:
        // ExecuteUpdate never reads or rewrites the roster.
        var db = Resolve<PlannerDbContext>();
        var name = req.Name!.Trim();
        var rows = await db.PersonalRosterSnapshots
            .Where(entity => entity.Id == snapshotId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.Name, name)
                .SetProperty(entity => entity.UpdatedAt, Resolve<TimeProvider>().GetUtcNow()), ct);
        if (rows == 0)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var snapshot = await db.PersonalRosterSnapshots.AsNoTracking()
            .Where(entity => entity.Id == snapshotId)
            .Select(entity => new PersonalRosterSnapshotResponse(
                entity.Id.Value, entity.Name, entity.Source, entity.TakenAt, entity.CharacterCount, entity.MowCount))
            .FirstAsync(ct);
        await Send.OkAsync(snapshot, ct);
    }
}

public sealed class DeletePersonalRosterSnapshotEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("me/roster-snapshots/{snapshotId}");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes one of the caller's personal snapshots.";
            summary.Description = "A hard delete with no trash or restore. The synced roster and run records are "
                + "not affected.";
            summary.Response(StatusCodes.Status204NoContent, "The snapshot was deleted.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No snapshot with this id belongs to the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || PersonalRosterSnapshotProblems.RouteId(this) is not { } snapshotId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var rows = await Resolve<PlannerDbContext>().PersonalRosterSnapshots
            .Where(entity => entity.Id == snapshotId)
            .ExecuteDeleteAsync(ct);
        if (rows == 0)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}

public sealed class KeepPersonalRosterSnapshotEndpoint : EndpointWithoutRequest<PersonalRosterSnapshotResponse>
{
    public override void Configure()
    {
        Post("me/roster-snapshots/{snapshotId}/keep");
        Summary(summary =>
        {
            summary.Summary = "Keeps one of the caller's automatic snapshots as a manual one.";
            summary.Description = "source becomes manual; the name, takenAt and roster do not change. It then counts "
                + "toward limit, and the automatic quota can no longer replace it.";
            summary.Response<PersonalRosterSnapshotResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status409Conflict,
                PersonalRosterSnapshotProblems.LimitReached + " (extensions limit, count) at the limit; "
                + PersonalRosterSnapshotProblems.NotAutomatic + " (current is the snapshot) when it is not automatic.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "No snapshot with this id belongs to the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || PersonalRosterSnapshotProblems.RouteId(this) is not { } snapshotId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        switch (await Resolve<PersonalRosterSnapshotService>().KeepAsync(snapshotId, ct))
        {
            case KeepPersonalRosterSnapshotResult.Kept kept:
                await Send.OkAsync(PersonalRosterSnapshotResponse.From(kept.Snapshot), ct);
                return;
            case KeepPersonalRosterSnapshotResult.LimitReached limit:
                await PersonalRosterSnapshotProblems.SendLimitReachedAsync(this, limit.Limit, limit.Count, ct);
                return;
            case KeepPersonalRosterSnapshotResult.NotAutomatic notAutomatic:
                await this.SendConflictAsync(
                    PersonalRosterSnapshotProblems.NotAutomatic,
                    "Only an automatic snapshot can be kept.",
                    PersonalRosterSnapshotResponse.From(notAutomatic.Snapshot),
                    ct);
                return;
            default:
                await this.SendNotFoundProblemAsync(ct);
                return;
        }
    }
}
