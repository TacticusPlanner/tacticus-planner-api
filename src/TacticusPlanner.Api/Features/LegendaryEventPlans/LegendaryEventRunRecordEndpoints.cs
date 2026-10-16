using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
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
            await Send.NotFoundAsync(ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter.
        var plans = await Resolve<PlannerDbContext>().LegendaryEventPlans.AsNoTracking().WithTeams().ToListAsync(ct);
        await Send.OkAsync(
            new LegendaryEventPlanListResponse(plans
                .OrderBy(plan => plan.EventId, StringComparer.Ordinal)
                .Select(LegendaryEventPlanProjection.Map)
                .ToList()),
            ct);
    }
}

public sealed class GetLegendaryEventRunRecordsEndpoint : EndpointWithoutRequest<LegendaryEventRunRecordsResponse>
{
    public override void Configure()
    {
        Get("me/legendary-event-plans/{eventId}/run-records");
        Summary(summary =>
        {
            summary.Summary = "Reads the caller's run records for a Legendary Event, newest first.";
            summary.Description = "Records are written by player sync only. Each embeds the run's roster snapshot, or "
                + "null when it was removed. A current catalog event with no records reads as an empty list; an event "
                + "that left the catalog keeps answering with its records.";
            summary.Response<LegendaryEventRunRecordsResponse>(StatusCodes.Status200OK);
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
            await Send.NotFoundAsync(ct);
            return;
        }

        var records = await Resolve<PlannerDbContext>().LegendaryEventRunRecords
            .AsNoTracking()
            .Include(record => record.RosterSnapshot)
            .Where(record => record.EventId == eventId)
            .OrderByDescending(record => record.StartedAt)
            .ToListAsync(ct);
        if (records.Count == 0 && Resolve<LegendaryEventCatalogValidator>().FindEvent(eventId) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new LegendaryEventRunRecordsResponse(eventId, records.Select(Map).ToList()), ct);
    }

    private static LegendaryEventRunRecordResponse Map(LegendaryEventRunRecord record) =>
        new(
            record.Id.Value,
            record.Run,
            record.StartedAt,
            record.ProgressAt,
            record.CatalogVersion,
            record.Points,
            new LegendaryEventRunRecordLanesResponse(
                MapLane(record.Lanes.Alpha), MapLane(record.Lanes.Beta), MapLane(record.Lanes.Gamma)),
            record.Teams
                .Select(team => new LegendaryEventRunRecordTeamResponse(
                    team.TeamId, team.LaneId, team.Name, team.SortOrder, team.MemberUnitIds, team.ReserveUnitId,
                    team.ObjectiveIndexes, team.ExpectedBattleClears, team.ExpectedBattleClearsSource))
                .ToList(),
            record.RosterSnapshot is { } snapshot
                ? new RosterSnapshotResponse(
                    snapshot.Id.Value, snapshot.TakenAt, snapshot.CatalogVersion, snapshot.Characters, snapshot.Mows)
                : null);

    private static List<LegendaryEventRunRecordEncounterResponse>? MapLane(LegendaryEventRunRecordLane? lane) =>
        lane?.Encounters
            .Select(encounter => new LegendaryEventRunRecordEncounterResponse(encounter.ObjectivesCleared, encounter.HighScore))
            .ToList();
}
