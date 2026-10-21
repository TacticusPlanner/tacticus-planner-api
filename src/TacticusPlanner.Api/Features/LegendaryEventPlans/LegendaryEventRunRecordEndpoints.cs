using System.Buffers.Text;
using System.Globalization;
using System.Text;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public sealed class ListLegendaryEventPlansEndpoint : EndpointWithoutRequest<LegendaryEventPlanListResponse>
{
    public override void Configure()
    {
        Get("me/legendary-event-plans");
        Summary(summary =>
        {
            summary.Summary = "Lists every stored plan of the caller's profile.";
            summary.Description = "Ordered by eventId (ordinal). Only stored plans are listed: a catalog event with no "
                + "plan written is not returned as an empty plan. Plans whose event left the catalog are included.";
            summary.Response<LegendaryEventPlanListResponse>(StatusCodes.Status200OK);
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
        var plans = await Resolve<PlannerDbContext>().LegendaryEventPlans.AsNoTracking().ToListAsync(ct);
        await Send.OkAsync(
            new LegendaryEventPlanListResponse(plans
                .OrderBy(plan => plan.EventId, StringComparer.Ordinal)
                .Select(plan => LegendaryEventPlanProjection.Map(plan))
                .ToList()),
            ct);
    }
}

public sealed class GetLegendaryEventRunRecordsEndpoint : EndpointWithoutRequest<LegendaryEventRunRecordsResponse>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public override void Configure()
    {
        Get("me/legendary-event-plans/{eventId}/run-records");
        Summary(summary =>
        {
            summary.Summary = "Reads a page of the caller's run records for a Legendary Event, newest first.";
            summary.Description = "Records are written by player sync only and are ordered by startedAt then id, both "
                + "descending. limit is 1–100 (default 20); after is the opaque nextAfter of the previous page, null "
                + "on the last one. Records carry no roster: read it from .../run-records/{recordId}/roster. A current "
                + "catalog event with no records reads as an empty page; an event that left the catalog keeps answering "
                + "with its records.";
            summary.Response<LegendaryEventRunRecordsResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status400BadRequest, "limit is out of range or after is not a valid cursor.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound,
                "The event is neither a current catalog Legendary Event nor has any record for the caller.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var eventId = Route<string>("eventId")!;
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var limitValue = HttpContext.Request.Query["limit"].ToString();
        var limit = DefaultLimit;
        if (limitValue.Length > 0 && (!int.TryParse(limitValue, out limit) || limit is < 1 or > MaxLimit))
            AddError(new ValidationFailure("limit", $"limit must be between 1 and {MaxLimit}."));
        var afterValue = HttpContext.Request.Query["after"].ToString();
        RunRecordCursor? after = null;
        if (afterValue.Length > 0 && !RunRecordCursor.TryDecode(afterValue, out after))
            AddError(new ValidationFailure("after", "after is not a valid cursor; pass the nextAfter of the previous page."));
        if (ValidationFailed)
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter. The next page continues strictly
        // after the cursor in (startedAt DESC, id DESC) order, as a row comparison the index serves.
        var db = Resolve<PlannerDbContext>();
        var records = after is { } cursor
            ? db.LegendaryEventRunRecords.FromSql(
                $"SELECT * FROM legendary_event_run_records WHERE (started_at, id) < ({cursor.StartedAt}, {cursor.Id})")
            : db.LegendaryEventRunRecords;
        var page = await records
            .AsNoTracking()
            .Where(record => record.EventId == eventId)
            .OrderByDescending(record => record.StartedAt)
            .ThenByDescending(record => record.Id)
            .Take(limit + 1)
            .Select(record => new
            {
                record.Id,
                record.Run,
                record.StartedAt,
                record.ProgressAt,
                record.CatalogVersion,
                record.Points,
                record.Lanes,
                record.Teams,
            })
            .ToListAsync(ct);
        if (page.Count == 0 && after is null && Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId) is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var hasMore = page.Count > limit;
        var items = page.Take(limit).ToList();
        await Send.OkAsync(
            new LegendaryEventRunRecordsResponse(
                eventId,
                items.Select(record => new LegendaryEventRunRecordResponse(
                    record.Id.Value,
                    record.Run,
                    record.StartedAt,
                    record.ProgressAt,
                    record.CatalogVersion,
                    record.Points,
                    new LegendaryEventRunRecordLanesResponse(
                        MapLane(record.Lanes.Alpha), MapLane(record.Lanes.Beta), MapLane(record.Lanes.Gamma)),
                    MapTeams(record.Teams, record.Run))).ToList(),
                hasMore ? new RunRecordCursor(items[^1].StartedAt, items[^1].Id.Value).Encode() : null),
            ct);
    }

    /// <summary>The served team shape: each team's depth is its depth for the record's run.</summary>
    private static List<LegendaryEventRunRecordTeamResponse> MapTeams(List<LegendaryEventTeamRecord> teams, int run) =>
        teams
            .Select(team =>
            {
                var depth = team.RunDepths.FirstOrDefault(entry => entry.Run == run);
                return new LegendaryEventRunRecordTeamResponse(
                    team.Id.Value, team.LaneId, team.Name, team.SortOrder, team.MemberUnitIds, team.ReserveUnitId,
                    team.ObjectiveIndexes, depth?.ExpectedBattleClears,
                    depth is null ? null : LegendaryEventPlanProjection.SourceName(depth.Source));
            })
            .ToList();

    private static List<LegendaryEventRunRecordEncounterResponse>? MapLane(LegendaryEventRunRecordLane? lane) =>
        lane?.Encounters
            .Select(encounter => new LegendaryEventRunRecordEncounterResponse(encounter.ObjectivesCleared, encounter.HighScore))
            .ToList();
}

public sealed class GetLegendaryEventRunRecordRosterEndpoint : EndpointWithoutRequest<RosterSnapshotResponse>
{
    public override void Configure()
    {
        Get("me/legendary-event-plans/{eventId}/run-records/{recordId}/roster");
        Summary(summary =>
        {
            summary.Summary = "Reads the roster snapshot of one of the caller's run records.";
            summary.Description = "The whole owned roster as of the record's last progress. The ETag changes whenever "
                + "a sync rewrites the roster; a matching If-None-Match answers 304.";
            summary.Response<RosterSnapshotResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status304NotModified, "The roster has not been rewritten since the given ETag.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound,
                "No record with this id belongs to the caller and the event.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var eventId = Route<string>("eventId")!;
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || !Guid.TryParse(Route<string>("recordId"), out var recordGuid))
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter. The record's UpdatedAt moves
        // exactly when a refresh rewrites it, roster included, so it is the ETag (no content hash needed).
        var recordId = LegendaryEventRunRecordId.From(recordGuid);
        var db = Resolve<PlannerDbContext>();
        var stamp = await db.LegendaryEventRunRecords.AsNoTracking()
            .Where(record => record.Id == recordId && record.EventId == eventId)
            .Select(record => (DateTimeOffset?)record.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (stamp is not { } updatedAt)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var etag = ETagHelper.CreateEtag(updatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        if (ETagHelper.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        var roster = await db.LegendaryEventRunRecords.AsNoTracking()
            .Where(record => record.Id == recordId)
            .Select(record => record.Roster)
            .FirstAsync(ct);
        HttpContext.Response.Headers.ETag = etag;
        HttpContext.Response.Headers.CacheControl = "private, no-cache";
        await Send.OkAsync(
            new RosterSnapshotResponse(roster.TakenAt, roster.CatalogVersion, roster.Characters, roster.Mows), ct);
    }
}

/// <summary>The opaque run-record page cursor: the UTC <c>startedAt</c> and id of the previous page's last record,
/// versioned and base64url-encoded.</summary>
public sealed record RunRecordCursor(DateTimeOffset StartedAt, Guid Id)
{
    private const string Version = "v1";

    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{Version}|{StartedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}|{Id:N}"));

    public static bool TryDecode(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RunRecordCursor? cursor)
    {
        cursor = null;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = text.Split('|');
        if (parts is not [Version, var startedAt, var id]
            || !DateTimeOffset.TryParseExact(startedAt, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsedStartedAt)
            || !Guid.TryParseExact(id, "N", out var parsedId))
        {
            return false;
        }

        cursor = new RunRecordCursor(parsedStartedAt.ToUniversalTime(), parsedId);
        return true;
    }
}
