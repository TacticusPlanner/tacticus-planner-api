using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Relational coverage for in-place target editing (<c>goal-target-editing</c>): the target lives in the
/// <c>config</c> jsonb and a target-only edit is a real revision for optimistic concurrency. No per-change history
/// is stored.
/// </summary>
public sealed class GoalTargetEditPostgresTests
{
    [Fact]
    public async Task ATargetOnlyEditIsARevisionAndUpgradeTargetsRoundTrip()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var goalId = await SeedAsync(database, new Goal(GoalEntityType.Character, "ragnar", GoalType.Upgrade)
        {
            Config = new GoalConfig
            {
                Upgrade = new UpgradeTarget { Targets = [new UpgradeMaterialTarget { UpgradeId = "upgHpC014", Quantity = 2 }] },
            },
        });

        await using (var db = database.CreateContext())
        {
            var goal = await db.Goals.SingleAsync(entity => entity.Id == goalId, ct);
            goal.Config.Upgrade!.Targets = [new UpgradeMaterialTarget { UpgradeId = "upgHpC014", Quantity = 9 }];
            goal.Config.Upgrade.RankRange = new UpgradeRange { Start = 1, End = 4 };
            db.Entry(goal).Property(entity => entity.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct);
        }

        await using var verify = database.CreateContext();
        var stored = await verify.Goals.SingleAsync(entity => entity.Id == goalId, ct);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(9, Assert.Single(stored.Config.Upgrade!.Targets).Quantity);
        Assert.Equal(4, stored.Config.Upgrade.RankRange!.End);
    }

    [Fact]
    public async Task TwoEditsFromTheSameRevisionCannotBothCommit()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var goalId = await SeedAsync(database, RankTargetSlotsPostgresTests.RankGoal(database.ProfileId, end: 5));

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstGoal = await first.Goals.SingleAsync(entity => entity.Id == goalId, ct);
        var secondGoal = await second.Goals.SingleAsync(entity => entity.Id == goalId, ct);

        firstGoal.Config.Rank!.End = 7;
        first.Entry(firstGoal).Property(entity => entity.UpdatedAt).IsModified = true;
        await first.SaveChangesAsync(ct);

        secondGoal.Config.Rank!.End = 6;
        second.Entry(secondGoal).Property(entity => entity.UpdatedAt).IsModified = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));

        await using var verify = database.CreateContext();
        Assert.Equal(7, (await verify.Goals.SingleAsync(entity => entity.Id == goalId, ct)).Config.Rank!.End);
    }

    private static Task<GoalId> SeedAsync(PostgresTestDatabase database, Goal goal)
    {
        goal.Id = GoalId.From(Guid.CreateVersion7());
        goal.ProfileId = database.ProfileId;
        goal.Status = GoalStatus.Active;
        goal.RefreshRankTargetKey();
        return RankTargetSlotsPostgresTests.AddAsync(database, goal);
    }
}
