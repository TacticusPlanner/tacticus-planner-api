using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence.Configurations;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// The in-flight slot indexes on <c>goals</c>, scoped by profile (<c>project-goal-slots</c>): a non-Rank goal holds
/// (unit, type), a Rank goal (unit, normalized end target), only while Active or Paused, whatever its projects.
/// </summary>
public sealed class RankTargetSlotsPostgresTests
{
    [Fact]
    public async Task SlotIndexesRejectDuplicatesAndAcceptDistinctRankTargets()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;

        await AddAsync(database, RankGoal(database.ProfileId, end: 12));
        // Point-five below Adamantine1 is the same end state as three applied slots: one normalized target.
        await AddAsync(database, RankGoal(database.ProfileId, end: 12, appliedUpgrades: 3));
        var duplicateRank = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(database, RankGoal(database.ProfileId, end: 12, pointFive: true)));
        Assert.True(ProjectGoalPlanningService.IsSlotViolation(duplicateRank));
        Assert.Equal(GoalSlotIndexes.RankTarget, (duplicateRank.InnerException as PostgresException)?.ConstraintName);

        await AddAsync(database, AbilityGoal(database.ProfileId));
        var duplicateSlot = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(database, AbilityGoal(database.ProfileId)));
        Assert.True(ProjectGoalPlanningService.IsSlotViolation(duplicateSlot));
        Assert.Equal(GoalSlotIndexes.Slot, (duplicateSlot.InnerException as PostgresException)?.ConstraintName);

        // Historical goals hold no slot, and another profile's goals never collide.
        await AddAsync(database, RankGoal(database.ProfileId, end: 12, status: GoalStatus.Completed));
        await AddAsync(database, AbilityGoal(database.ProfileId, GoalStatus.Archived));
        var otherProfile = ProfileId.From(Guid.CreateVersion7());
        await database.SeedProfileAsync(otherProfile);
        await AddAsync(database, RankGoal(otherProfile, end: 12), otherProfile);
        await AddAsync(database, AbilityGoal(otherProfile), otherProfile);

        await using var db = database.CreateContext();
        Assert.Equal(
            ["12:0", "12:3", "12:0"],
            await db.Goals.Where(goal => goal.GoalType == GoalType.Rank).OrderBy(goal => goal.CreatedAt)
                .Select(goal => goal.RankTargetKey!).ToListAsync(ct));
    }

    /// <summary>Hard delete (what <c>DeleteGoalEndpoint</c> does) cascades the goal's memberships and frees its
    /// slot, so a recreation is a brand-new goal with only the explicitly requested memberships.</summary>
    [Fact]
    public async Task DeletingARankGoalReleasesItsSlotAndRecreationIsFresh()
    {
        await using var database = await PostgresTestDatabase.StartAsync(projects: 2);
        var ct = TestContext.Current.CancellationToken;

        var original = await AddAsync(database, RankGoal(database.ProfileId, end: 12, status: GoalStatus.Paused), memberOf: database.ProjectIds);
        await using (var db = database.CreateContext())
        {
            db.Goals.Remove(await db.Goals.SingleAsync(goal => goal.Id == original, ct));
            await db.SaveChangesAsync(ct);
        }

        var recreated = await AddAsync(database, RankGoal(database.ProfileId, end: 12), memberOf: database.ProjectIds);

        await using var verify = database.CreateContext();
        Assert.NotEqual(original, recreated);
        Assert.Equal(0, await verify.ProjectGoals.CountAsync(entry => entry.GoalId == original, ct));
        Assert.Equal(2, await verify.ProjectGoals.CountAsync(entry => entry.GoalId == recreated, ct));
        var goal = await verify.Goals.SingleAsync(entry => entry.Id == recreated, ct);
        Assert.Equal(GoalStatus.Active, goal.Status);
        Assert.Equal("12:0", goal.RankTargetKey);
    }

    internal static Goal RankGoal(
        ProfileId profileId, int end, bool pointFive = false, int appliedUpgrades = 0,
        GoalStatus status = GoalStatus.Active, string unit = "ragnar")
    {
        var goal = new Goal(GoalEntityType.Character, unit, GoalType.Rank)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Config = new GoalConfig
            {
                Rank = new RankTarget { Start = 1, End = end, EndPointFive = pointFive, EndAppliedUpgrades = appliedUpgrades },
            },
        };
        goal.RefreshRankTargetKey();
        return goal;
    }

    private static Goal AbilityGoal(ProfileId profileId, GoalStatus status = GoalStatus.Active) =>
        new(GoalEntityType.Mow, "forgefiend", GoalType.Ability)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Config = new GoalConfig { Ability = new AbilityTarget { ActiveStart = 1, ActiveEnd = 2, PassiveStart = 1, PassiveEnd = 2 } },
        };

    /// <summary>Saves one goal straight through EF (no friendly pre-check), in the profile's order when in
    /// flight and in the given projects.</summary>
    internal static async Task<GoalId> AddAsync(
        PostgresTestDatabase database, Goal goal, ProfileId? profileId = null, IReadOnlyList<ProjectId>? memberOf = null)
    {
        await using var db = database.CreateContext(profileId ?? database.ProfileId);
        db.Goals.Add(goal);
        if (ProjectGoalPlanningService.IsInFlight(goal.Status))
            await new GoalOrderService(db).AppendAsync(goal, TestContext.Current.CancellationToken);
        foreach (var projectId in memberOf ?? [])
            db.ProjectGoals.Add(ProjectGoalPlanningService.CreateMembership(projectId, goal.Id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return goal.Id;
    }
}
