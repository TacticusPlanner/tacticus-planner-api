using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Teams;

public sealed class TeamOptions
{
    public const string SectionName = "Teams";

    /// <summary>How many teams a profile may hold; read by creates and the V1 import alike (design D2).</summary>
    [Range(1, 1000)]
    public int Limit { get; set; } = TeamValidation.DefaultLimit;
}

/// <summary>
/// Shared teams logic (<c>personal-teams</c>): the ordered list, the rule facts of the current catalog, mutations
/// under the profile lock (design D2), and renumbering. Positions are written with a single <c>UPDATE</c> that bypasses
/// the change tracker, so a reorder or a closed gap changes no team's revision or other fields.
/// </summary>
public sealed class TeamsService(PlannerDbContext db, IGameCatalogProvider catalog, IOptions<TeamOptions> options)
{
    public const string NameTaken = "teamNameTaken";
    public const string LimitReached = "teamLimitReached";
    public const string RulesBroken = "teamRulesBroken";
    public const string RevisionStale = "teamRevisionStale";
    public const string OrderStale = "teamOrderStale";

    private const string NameIndex = "ux_teams_profile_id_name_key";

    public int Limit => options.Value.Limit;

    public TeamRuleFacts Facts => TeamCatalogFacts.For(catalog.Current);

    /// <summary>The profile's teams in order; scoped by the query filter.</summary>
    public Task<List<Team>> ListAsync(CancellationToken ct, bool tracked = false)
    {
        var query = tracked ? db.Teams : db.Teams.AsNoTracking();
        return query.OrderBy(team => team.SortOrder).ThenBy(team => team.Id).ToListAsync(ct);
    }

    public async Task<ListTeamsResponse> ListResponseAsync(CancellationToken ct)
    {
        var facts = Facts;
        return new ListTeamsResponse([.. (await ListAsync(ct)).Select(team => TeamResponse.From(team, facts))]);
    }

    /// <summary>Whether another of the profile's teams already uses <paramref name="name"/>, ignoring case.</summary>
    public Task<bool> IsNameTakenAsync(string name, TeamId? except, CancellationToken ct)
    {
        var key = TeamValidation.NameKey(name);
        return db.Teams.AnyAsync(team => team.NameKey == key && (except == null || team.Id != except), ct);
    }

    public static bool IsNameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == NameIndex;

    /// <summary>
    /// Runs <paramref name="mutation"/> in a READ COMMITTED transaction holding the profile row lock, as
    /// <c>ProjectGoalPlanningService.ExecuteLockedMutationAsync</c> does: every count and position read inside it sees
    /// what a predecessor committed, so the limit and positions hold under concurrency.
    /// </summary>
    public async Task<T> ExecuteLockedAsync<T>(Func<IDbContextTransaction, Task<T>> mutation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.LockCurrentProfileAsync(ct);
            return await mutation(transaction);
        });
    }

    /// <summary>Writes positions 1..n in <paramref name="orderedIds"/> order, in one statement.</summary>
    public Task WritePositionsAsync(ProfileId profileId, IReadOnlyList<TeamId> orderedIds, CancellationToken ct)
    {
        if (orderedIds.Count == 0)
            return Task.CompletedTask;
        var ids = orderedIds.Select(id => id.Value).ToArray();
        var positions = Enumerable.Range(1, ids.Length).ToArray();
        var profile = profileId.Value;
        return db.Database.ExecuteSqlAsync(
            $"""
            UPDATE teams SET sort_order = v.position
            FROM unnest({ids}, {positions}) AS v(id, position)
            WHERE teams.id = v.id AND teams.profile_id = {profile} AND teams.sort_order <> v.position
            """,
            ct);
    }
}
