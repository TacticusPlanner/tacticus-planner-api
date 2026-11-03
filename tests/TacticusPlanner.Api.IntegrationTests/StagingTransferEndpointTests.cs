using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.StagingTransfer;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Domain.UserSettings;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>
/// Production (source) and staging (target) as two hosts with their own databases. The target's
/// staging-transfer HTTP client is wired to the source's in-memory server, so the real client, service-key
/// check, code sealing, export and import all run end to end.
/// </summary>
public sealed class StagingTransferEndpointTests : IAsyncDisposable
{
    private const string ServiceKey = "test-staging-transfer-service-key";

    private readonly PlannerApiFactory source = new();
    private readonly PlannerApiFactory target = new();

    public StagingTransferEndpointTests()
    {
        source.ExtraSettings["StagingTransfer:ServiceKey"] = ServiceKey;

        target.ExtraSettings["StagingTransfer:ServiceKey"] = ServiceKey;
        target.ExtraSettings["StagingTransfer:SourceBaseUrl"] = "http://localhost/";
        target.ExtraServices = services => services
            .AddHttpClient(StagingTransferSourceClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => source.Server.CreateHandler());
    }

    public async ValueTask DisposeAsync()
    {
        await source.DisposeAsync();
        await target.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ImportReplacesStagingDataWithTheProductionProfile()
    {
        var sourceSubject = $"prod-{Guid.NewGuid()}";
        var sourceClient = await GoalsTestHelpers.CreateProvisionedClientAsync(source, sourceSubject);
        var seeded = await SeedSourceAsync(sourceSubject);

        var targetSubject = $"stg-{Guid.NewGuid()}";
        var targetClient = await GoalsTestHelpers.CreateProvisionedClientAsync(target, targetSubject);
        var staleGoalIds = await GoalsTestHelpers.SeedRankGoalsAsync(target, targetSubject, 2, GoalStatus.Completed);

        var codeResponse = await sourceClient.PostAsync("/api/v1/account/staging-transfer-code", null, Ct);
        codeResponse.EnsureSuccessStatusCode();
        var code = await codeResponse.Content.ReadFromJsonAsync<CreateStagingTransferCodeResponse>(Ct);
        Assert.NotNull(code);

        var importResponse = await targetClient.PostAsJsonAsync(
            "/api/v1/account/import-from-production", new ImportFromProductionRequest(code.Code), Ct);
        importResponse.EnsureSuccessStatusCode();
        var counts = await importResponse.Content.ReadFromJsonAsync<ImportFromProductionResponse>(Ct);
        // Two projects: the seeded one and the Default project provisioned with the production account.
        Assert.Equal(new ImportFromProductionResponse(2, 2, 1, 1, 1, true, true), counts);

        using var scope = target.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profile = await db.Profiles.IgnoreQueryFilters().AsNoTracking().FirstAsync(entity => entity.Subject == targetSubject, Ct);
        var goals = await db.Goals.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profile.Id).ToListAsync(Ct);

        Assert.DoesNotContain(goals, goal => staleGoalIds.Contains(goal.Id.Value));
        Assert.Equal(2, goals.Count);
        Assert.DoesNotContain(goals, goal => goal.Id == seeded.ParentGoalId || goal.Id == seeded.ChildGoalId);
        var parent = Assert.Single(goals, goal => goal.EntityId == "blackTerminator");
        var child = Assert.Single(goals, goal => goal.EntityId == "ultramarinesCalgar");
        Assert.Equal(GoalStatus.Active, parent.Status);
        Assert.Equal(1, parent.GlobalPriority);
        Assert.Equal("prod notes", parent.Notes);
        Assert.Equal(12, parent.Config.Rank!.End);
        Assert.Equal([parent.Id.Value], child.DependsOn);

        var projects = await db.Projects.IgnoreQueryFilters().AsNoTracking()
            .Include(entity => entity.ProjectGoals)
            .Where(entity => entity.ProfileId == profile.Id).ToListAsync(Ct);
        Assert.Single(projects, entity => entity.Type == ProjectType.Default);
        var project = Assert.Single(projects, entity => entity.Type == ProjectType.Custom);
        Assert.Equal("Prod project", project.Name);
        Assert.Equal(
            [parent.Id, child.Id],
            project.ProjectGoals.OrderBy(link => link.CreatedAt).Select(link => link.GoalId).ToList());

        var plan = Assert.Single(await db.LegendaryEventPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profile.Id).ToListAsync(Ct));
        Assert.Equal("testEvent", plan.EventId);
        Assert.Equal("Alpha team", Assert.Single(plan.Teams).Name);

        var record = Assert.Single(await db.LegendaryEventRunRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profile.Id).ToListAsync(Ct));
        Assert.Equal(1234, record.Points);

        var snapshot = Assert.Single(await db.PersonalRosterSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profile.Id).ToListAsync(Ct));
        Assert.Equal(("Before LE", RosterSnapshotSource.Manual, seeded.SnapshotTakenAt),
            (snapshot.Name, snapshot.Source, snapshot.TakenAt));
        Assert.Equal("ultraCalgar", Assert.Single(snapshot.Roster.Characters).UnitId.Value);
        Assert.Equal(1, snapshot.CharacterCount);

        var settings = await db.UserSettings.IgnoreQueryFilters().AsNoTracking().FirstAsync(entity => entity.Id == profile.Id, Ct);
        Assert.Equal(538, settings.Settings.DailyEnergy);

        var overrides = await db.PlayerDataOverrides.IgnoreQueryFilters().AsNoTracking().FirstAsync(entity => entity.Id == profile.Id, Ct);
        Assert.Equal("Xenos", Assert.Single(overrides.OnslaughtProgressOverrides).Alliance);
    }

    [Fact]
    public async Task ImportRejectsAnInvalidCode()
    {
        var targetClient = await GoalsTestHelpers.CreateProvisionedClientAsync(target);

        var response = await targetClient.PostAsJsonAsync(
            "/api/v1/account/import-from-production", new ImportFromProductionRequest("TP1-not-a-real-code"), Ct);

        await response.AssertFieldRejectedAsync("code");
    }

    [Fact]
    public async Task ExportRefusesAWrongServiceKey()
    {
        var sourceClient = await GoalsTestHelpers.CreateProvisionedClientAsync(source);
        var codeResponse = await sourceClient.PostAsync("/api/v1/account/staging-transfer-code", null, Ct);
        var code = await codeResponse.Content.ReadFromJsonAsync<CreateStagingTransferCodeResponse>(Ct);

        var anonymous = source.CreateClient();
        anonymous.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");
        anonymous.DefaultRequestHeaders.Add(StagingTransferOptions.ServiceKeyHeaderName, "wrong-key");
        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/" + ExportForStagingEndpoint.Route, new ExportForStagingRequest(code!.Code), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EachSideServesOnlyItsOwnEndpoints()
    {
        var sourceClient = await GoalsTestHelpers.CreateProvisionedClientAsync(source);
        var targetClient = await GoalsTestHelpers.CreateProvisionedClientAsync(target);

        var importOnSource = await sourceClient.PostAsJsonAsync(
            "/api/v1/account/import-from-production", new ImportFromProductionRequest("TP1-x"), Ct);
        var codeOnTarget = await targetClient.PostAsync("/api/v1/account/staging-transfer-code", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, importOnSource.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, codeOnTarget.StatusCode);
    }

    private sealed record SeededSource(GoalId ParentGoalId, GoalId ChildGoalId, DateTimeOffset SnapshotTakenAt);

    private async Task<SeededSource> SeedSourceAsync(string subject)
    {
        using var scope = source.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = (await db.Profiles.IgnoreQueryFilters().FirstAsync(entity => entity.Subject == subject, Ct)).Id;

        var parent = new Goal(GoalEntityType.Character, "blackTerminator", GoalType.Rank)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = GoalStatus.Active,
            GlobalPriority = 1,
            Notes = "prod notes",
            Config = new GoalConfig { Rank = new RankTarget { Start = 3, End = 12 } },
        };
        var child = new Goal(GoalEntityType.Character, "ultramarinesCalgar", GoalType.Rank)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = GoalStatus.Completed,
            Config = new GoalConfig { Rank = new RankTarget { Start = 0, End = 15 } },
            DependsOn = [Guid.NewGuid()],
        };
        child.DependsOn.Add(parent.Id.Value);
        db.Goals.AddRange(parent, child);

        var project = new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = "Prod project",
            Status = ProjectStatus.Active,
            Type = ProjectType.Custom,
        };
        var linkedAt = DateTimeOffset.UtcNow;
        project.ProjectGoals.Add(new ProjectGoal { ProjectId = project.Id, GoalId = parent.Id, CreatedAt = linkedAt });
        project.ProjectGoals.Add(new ProjectGoal { ProjectId = project.Id, GoalId = child.Id, CreatedAt = linkedAt.AddSeconds(1) });
        db.Projects.Add(project);

        db.LegendaryEventPlans.Add(new LegendaryEventPlan
        {
            Id = LegendaryEventPlanId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EventId = "testEvent",
            CatalogVersion = "1",
            Teams = [new LegendaryEventTeamRecord { Id = LegendaryEventTeamId.From(Guid.CreateVersion7()), LaneId = "alpha", Name = "Alpha team" }],
        });
        db.LegendaryEventRunRecords.Add(new LegendaryEventRunRecord
        {
            Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EventId = "testEvent",
            Run = 1,
            StartedAt = linkedAt,
            ProgressAt = linkedAt,
            CatalogVersion = "1",
            Points = 1234,
        });
        var takenAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(
            profileId,
            "Before LE",
            RosterSnapshotSource.Manual,
            new RosterSnapshot
            {
                TakenAt = takenAt,
                CatalogVersion = "1",
                Characters = [new PlayerCharacterRecord { UnitId = UnitId.From("ultraCalgar"), Rank = (UnitRank)12 }],
            }));
        db.UserSettings.Add(new UserSettings { Id = profileId, Settings = new UserSettingsData { DailyEnergy = 538 } });
        db.PlayerDataOverrides.Add(new PlayerDataOverride
        {
            Id = profileId,
            OnslaughtProgressOverrides = [new OnslaughtProgressOverrideRecord { Alliance = "Xenos", Sector = "Iron", Tier = 2 }],
        });

        await db.SaveChangesAsync(Ct);
        return new SeededSource(parent.Id, child.Id, takenAt);
    }
}
