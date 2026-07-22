using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Planning;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Exercises PlannerDbContext's global profile query filters (see
/// PlannerDbContext.ApplyProfileQueryFilters) directly against a fixed <see cref="ICurrentProfileProvider"/>
/// rather than through HTTP endpoints — isolation must hold at the DbContext level itself, independent of
/// any particular endpoint remembering to scope its query.
/// </summary>
public sealed class PlannerDbContextQueryFilterTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GoalsProjectsPlanningSettingsAndProjectGoalsAreIsolatedBetweenProfiles()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();

        var profileA = ProfileId.From(Guid.CreateVersion7());
        var profileB = ProfileId.From(Guid.CreateVersion7());
        await SeedProfileAsync(scope, profileA, ct);
        await SeedProfileAsync(scope, profileB, ct);

        var goalA = new Goal
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileA,
            EntityType = GoalEntityType.Character,
            EntityId = "blackTerminator",
            GoalType = GoalType.Rank,
            Status = GoalStatus.Active,
        };
        var projectA = new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileA,
            Name = "A's project",
            Status = ProjectStatus.Active,
        };
        var settingsA = new PlanningSettings { Id = profileA, DailyEnergy = 378 };

        // Seeded through an unfiltered context (CurrentProfileId null) — query filters only ever narrow
        // reads, never writes, so this is just about giving the rows a known owner.
        using (var seedDb = CreateContext(scope, currentProfileId: null))
        {
            seedDb.Goals.Add(goalA);
            seedDb.Projects.Add(projectA);
            seedDb.PlanningSettings.Add(settingsA);
            seedDb.ProjectGoals.Add(new ProjectGoal { ProjectId = projectA.Id, GoalId = goalA.Id, Priority = 1 });
            await seedDb.SaveChangesAsync(ct);
        }

        using var dbA = CreateContext(scope, profileA);
        using var dbB = CreateContext(scope, profileB);

        var goalsA = await dbA.Goals.ToListAsync(ct);
        var goalsB = await dbB.Goals.ToListAsync(ct);
        Assert.Contains(goalsA, goal => goal.Id == goalA.Id);
        Assert.DoesNotContain(goalsB, goal => goal.Id == goalA.Id);

        var projectsA = await dbA.Projects.ToListAsync(ct);
        var projectsB = await dbB.Projects.ToListAsync(ct);
        Assert.Contains(projectsA, project => project.Id == projectA.Id);
        Assert.DoesNotContain(projectsB, project => project.Id == projectA.Id);

        // PlanningSettings' primary key IS the ProfileId — B querying for A's own settings row by A's id
        // must still come back empty, proving the filter (not just the predicate) is what's excluding it.
        var settingsVisibleToA = await dbA.PlanningSettings.FirstOrDefaultAsync(entity => entity.Id == profileA, ct);
        var settingsVisibleToB = await dbB.PlanningSettings.FirstOrDefaultAsync(entity => entity.Id == profileA, ct);
        Assert.NotNull(settingsVisibleToA);
        Assert.Null(settingsVisibleToB);

        // ProjectGoal carries no ProfileId of its own — it's scoped through its parent Project navigation
        // (see ApplyProfileQueryFilters), so this is the join-table case.
        var projectGoalsA = await dbA.ProjectGoals.ToListAsync(ct);
        var projectGoalsB = await dbB.ProjectGoals.ToListAsync(ct);
        Assert.Contains(projectGoalsA, entry => entry.ProjectId == projectA.Id && entry.GoalId == goalA.Id);
        Assert.DoesNotContain(projectGoalsB, entry => entry.ProjectId == projectA.Id);
    }

    /// <summary>
    /// Architecture guardrail: every entity that is profile-owned (its primary key IS a ProfileId, or it
    /// carries a ProfileId property) must have a global query filter configured. Catches a future
    /// profile-owned entity shipping without one — the failure message names the offending entity so the
    /// fix (add it to PlannerDbContext.ApplyProfileQueryFilters) is obvious.
    /// </summary>
    [Fact]
    public void EveryProfileOwnedEntityHasAQueryFilter()
    {
        using var scope = factory.Services.CreateScope();
        using var db = CreateContext(scope, currentProfileId: null);

        var profileOwnedEntityTypes = db.Model.GetEntityTypes()
            .Where(entityType => entityType.ClrType != typeof(Profile) // the tenant root itself, checked below
                && (typeof(BaseEntity<ProfileId>).IsAssignableFrom(entityType.ClrType)
                    || entityType.ClrType.GetProperty(nameof(Goal.ProfileId))?.PropertyType == typeof(ProfileId)))
            .ToList();

        // Sanity-check the guardrail itself: if this comes back empty, the discovery predicate above is
        // broken (e.g. renamed property), not that there are genuinely no profile-owned entities.
        Assert.NotEmpty(profileOwnedEntityTypes);

        var unfiltered = profileOwnedEntityTypes
            .Where(entityType => entityType.GetDeclaredQueryFilters().Count == 0)
            .Select(entityType => entityType.ClrType.Name)
            .ToList();
        Assert.True(unfiltered.Count == 0, $"Missing a global query filter for: {string.Join(", ", unfiltered)}");

        // Profile itself, and the ProfileId-less join tables scoped via a parent navigation, are covered
        // by name since they don't match the property-shape check above.
        Assert.NotEmpty(db.Model.FindEntityType(typeof(Profile))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(db.Model.FindEntityType(typeof(ProjectGoal))!.GetDeclaredQueryFilters());
        Assert.NotEmpty(db.Model.FindEntityType(typeof(ProjectTeam))!.GetDeclaredQueryFilters());
    }

    private static PlannerDbContext CreateContext(IServiceScope scope, ProfileId? currentProfileId)
    {
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<PlannerDbContext>>();
        var encryption = scope.ServiceProvider.GetRequiredService<IColumnEncryptionService>();
        return new PlannerDbContext(options, encryption, new FixedProfileProvider(currentProfileId));
    }

    private static async Task SeedProfileAsync(IServiceScope scope, ProfileId profileId, CancellationToken ct)
    {
        using var db = CreateContext(scope, currentProfileId: null);
        var accountId = AccountId.From(Guid.CreateVersion7());
        db.Accounts.Add(new Account
        {
            Id = accountId,
            Issuer = "https://example.ciamlogin.com/example.onmicrosoft.com/v2.0",
            Subject = $"filter-test-{profileId.Value}",
        });
        db.Profiles.Add(new Profile
        {
            Id = profileId,
            AccountId = accountId,
            DisplayName = "Filter Test User",
        });
        await db.SaveChangesAsync(ct);
    }

    private sealed class FixedProfileProvider(ProfileId? profileId) : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => profileId;
    }
}
