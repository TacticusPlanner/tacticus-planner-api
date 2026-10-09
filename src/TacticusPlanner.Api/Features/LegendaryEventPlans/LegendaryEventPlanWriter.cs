using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>What a mutation did to the loaded plan; the writer decides what to save and answer from it.</summary>
public enum LegendaryEventMutationOutcome
{
    /// <summary>Something changed: the plan is saved and its revision bumped.</summary>
    Changed,

    /// <summary>Nothing to save (a reorder that is already current); the plan is answered as it stands.</summary>
    Unchanged,

    TeamNotFound,

    OrderSetMismatch,
}

public abstract record LegendaryEventPlanWriteResult
{
    public sealed record Ok(LegendaryEventPlanResponse Plan) : LegendaryEventPlanWriteResult;

    /// <summary><c>expectedRevision</c> did not match, or a concurrent writer won at save time; carries the
    /// plan as it stands now.</summary>
    public sealed record Stale(LegendaryEventPlanResponse Plan) : LegendaryEventPlanWriteResult;

    public sealed record OrderSetMismatch(LegendaryEventPlanResponse Plan) : LegendaryEventPlanWriteResult;

    public sealed record TeamNotFound : LegendaryEventPlanWriteResult;
}

/// <summary>
/// Runs one plan mutation under the plan's single revision (design D4): load-or-new, check
/// <c>expectedRevision</c> (0 for a plan that does not exist yet), apply, stamp the catalog version, mark
/// the plan row modified so <c>EntityMetadataInterceptor</c> bumps its revision, save inside one transaction,
/// and answer with the whole plan. A concurrency failure at save time — the revision token, or the unique
/// <c>(profile_id, event_id)</c> index when two callers create the missing plan at once — is answered as
/// <see cref="LegendaryEventPlanWriteResult.Stale"/> carrying the winner's plan.
/// </summary>
public sealed class LegendaryEventPlanWriter(
    PlannerDbContext db,
    LegendaryEventPlanProjection projection,
    TimeProvider timeProvider)
{
    private const string UniqueViolation = "23505";

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public async Task<LegendaryEventPlanWriteResult> WriteAsync(
        ProfileId profileId,
        string eventId,
        long expectedRevision,
        Func<LegendaryEventPlan, LegendaryEventMutationOutcome> mutate,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            return await WriteOnceAsync(profileId, eventId, expectedRevision, mutate, transactional: false, ct);
        }

        // EF Core requires a user transaction to be opened inside the strategy delegate; each attempt starts
        // from a cleared tracker so a retried delegate re-reads rather than re-adding tracked entities.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
            await WriteOnceAsync(profileId, eventId, expectedRevision, mutate, transactional: true, ct));
    }

    private async Task<LegendaryEventPlanWriteResult> WriteOnceAsync(
        ProfileId profileId,
        string eventId,
        long expectedRevision,
        Func<LegendaryEventPlan, LegendaryEventMutationOutcome> mutate,
        bool transactional,
        CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = transactional ? await db.Database.BeginTransactionAsync(ct) : null;

        var plan = await projection.LoadTrackedAsync(eventId, ct);
        var isNew = plan is null;
        plan ??= new LegendaryEventPlan
        {
            Id = LegendaryEventPlanId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EventId = eventId,
        };

        if (plan.Revision != expectedRevision)
        {
            return new LegendaryEventPlanWriteResult.Stale(LegendaryEventPlanProjection.ToResponse(plan));
        }

        switch (mutate(plan))
        {
            case LegendaryEventMutationOutcome.TeamNotFound:
                return new LegendaryEventPlanWriteResult.TeamNotFound();
            case LegendaryEventMutationOutcome.OrderSetMismatch:
                return new LegendaryEventPlanWriteResult.OrderSetMismatch(LegendaryEventPlanProjection.ToResponse(plan));
            case LegendaryEventMutationOutcome.Unchanged:
                return new LegendaryEventPlanWriteResult.Ok(LegendaryEventPlanProjection.ToResponse(plan));
        }

        plan.CatalogVersion = GameCatalogRelease.Version;
        if (isNew)
        {
            db.LegendaryEventPlans.Add(plan);
        }
        else
        {
            // Child-row changes alone never mark the root modified; every mutation is a plan write.
            db.Entry(plan).Property(entity => entity.UpdatedAt).IsModified = true;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || (exception is DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolation } }))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
            }

            db.ChangeTracker.Clear();
            return new LegendaryEventPlanWriteResult.Stale(await projection.ReadAsync(eventId, ct));
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return new LegendaryEventPlanWriteResult.Ok(LegendaryEventPlanProjection.ToResponse(plan));
    }

    // ----- Mutation building blocks shared by the endpoints and the V1 import -----

    /// <summary>Appends a team at the end of its lane (<c>max + 1</c>, design D5).</summary>
    public static LegendaryEventTeam AppendTeam(
        LegendaryEventPlan plan,
        string laneId,
        LegendaryEventTeamContent content,
        LegendaryEventRunDepthWrite? depth,
        DateTimeOffset now)
    {
        var laneTeams = plan.Teams.Where(team => team.LaneId == laneId).ToList();
        var team = new LegendaryEventTeam
        {
            Id = LegendaryEventTeamId.From(Guid.CreateVersion7()),
            PlanId = plan.Id,
            LaneId = laneId,
            Name = content.Name,
            SortOrder = laneTeams.Count == 0 ? 0 : laneTeams.Max(existing => existing.SortOrder) + 1,
        };
        ApplyContent(team, content);
        if (depth is not null)
        {
            ApplyRunDepth(team, depth, now);
        }

        plan.Teams.Add(team);
        return team;
    }

    /// <summary>Replaces name, members, reserve and objectives; lane and order are untouched. Members and
    /// objectives are replaced wholesale (delete then insert) rather than edited in place, so swapping two
    /// units' positions never trips the unique <c>(team_id, unit_id)</c> index mid-statement.</summary>
    public static void ApplyContent(LegendaryEventTeam team, LegendaryEventTeamContent content)
    {
        team.Name = content.Name;
        team.Members.Clear();
        for (var position = 0; position < content.MemberUnitIds.Count; position++)
        {
            team.Members.Add(new LegendaryEventTeamMember
            {
                TeamId = team.Id,
                Reserve = false,
                Position = position,
                UnitId = content.MemberUnitIds[position],
            });
        }

        if (content.ReserveUnitId is { } reserve)
        {
            team.Members.Add(new LegendaryEventTeamMember { TeamId = team.Id, Reserve = true, Position = 0, UnitId = reserve });
        }

        team.Objectives.Clear();
        foreach (var index in content.ObjectiveIndexes.Distinct().Order())
        {
            team.Objectives.Add(new LegendaryEventTeamObjective { TeamId = team.Id, ObjectiveIndex = index });
        }
    }

    /// <summary>Upserts (non-null depth) or deletes (null depth) the row for <paramref name="depth"/>'s run,
    /// leaving every other run's row alone. <c>recorded_at</c> is refreshed on every upsert.</summary>
    public static void ApplyRunDepth(LegendaryEventTeam team, LegendaryEventRunDepthWrite depth, DateTimeOffset now)
    {
        var existing = team.RunDepths.FirstOrDefault(row => row.Run == depth.Run);
        if (depth.ExpectedBattleClears is not { } clears)
        {
            if (existing is not null)
            {
                team.RunDepths.Remove(existing);
            }

            return;
        }

        if (existing is null)
        {
            existing = new LegendaryEventTeamRunDepth { TeamId = team.Id, Run = depth.Run };
            team.RunDepths.Add(existing);
        }

        existing.ExpectedBattleClears = clears;
        existing.Source = depth.Source ?? LegendaryEventDepthSource.Manual;
        existing.RecordedAt = now;
    }

    /// <summary>Removes a team and re-densifies its lane's order.</summary>
    public static LegendaryEventMutationOutcome RemoveTeam(LegendaryEventPlan plan, LegendaryEventTeamId teamId)
    {
        var team = plan.Teams.FirstOrDefault(candidate => candidate.Id == teamId);
        if (team is null)
        {
            return LegendaryEventMutationOutcome.TeamNotFound;
        }

        plan.Teams.Remove(team);
        Densify(plan.Teams.Where(candidate => candidate.LaneId == team.LaneId).OrderBy(candidate => candidate.SortOrder));
        return LegendaryEventMutationOutcome.Changed;
    }

    /// <summary>Replaces a lane's order with <paramref name="teamIds"/>, which must be exactly that lane's
    /// team set with no repeats; an order that is already current changes nothing.</summary>
    public static LegendaryEventMutationOutcome Reorder(LegendaryEventPlan plan, string laneId, IReadOnlyList<LegendaryEventTeamId> teamIds)
    {
        var laneTeams = plan.Teams.Where(team => team.LaneId == laneId).OrderBy(team => team.SortOrder).ToList();
        if (teamIds.Distinct().Count() != teamIds.Count
            || teamIds.Count != laneTeams.Count
            || teamIds.Any(id => laneTeams.All(team => team.Id != id)))
        {
            return LegendaryEventMutationOutcome.OrderSetMismatch;
        }

        var byId = laneTeams.ToDictionary(team => team.Id);
        var requested = teamIds.Select(id => byId[id]).ToList();
        if (requested.SequenceEqual(laneTeams) && laneTeams.Select(team => team.SortOrder).SequenceEqual(Enumerable.Range(0, laneTeams.Count)))
        {
            return LegendaryEventMutationOutcome.Unchanged;
        }

        Densify(requested);
        return LegendaryEventMutationOutcome.Changed;
    }

    private static void Densify(IEnumerable<LegendaryEventTeam> ordered)
    {
        var sortOrder = 0;
        foreach (var team in ordered)
        {
            team.SortOrder = sortOrder++;
        }
    }
}
