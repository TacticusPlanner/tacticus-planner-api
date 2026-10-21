using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Coverage for <c>goal-target-editing</c>: the target section of <c>PUT /me/goals/{id}</c> replaces an
/// in-flight goal's end target in place, revision-checked, with creation-equivalent validation and the per-profile
/// slot check. No per-change history is stored.</summary>
public sealed class GoalTargetEditEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";
    private const string RelevantUpgradeId = "upgHpC014"; // on blackTerminator's rank-up ladder
    private const string UnrelatedUpgradeId = "upgArmC001";

    private static CreateGoalRequest Goal(string goalType, CreateGoalConfigRequest config) =>
        new("character", CharacterId, goalType, config, null);

    private static readonly CreateGoalRequest RankGoal =
        Goal("rank", new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)));

    private static UpdateGoalTargetRequest RankEdit(long revision, int end, bool pointFive = false, int applied = 0) =>
        new(revision, new GoalTargetEditRequest(Rank: new RankEndTargetRequest(end, pointFive, applied)));

    [Fact]
    public async Task RankTargetAdvancesInPlaceKeepingIdentityMembershipsOrderAndSnapshot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var created = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId), new ProjectPriorityRequest(projectB.ProjectId)],
                Snapshot = new CreateGoalSnapshotRequest(InitialRank: "Stone1"),
            });
        var other = await CreateGoalAsync(
            client,
            Goal("ascension", new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"))) with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId)],
            });
        var orderBefore = await PrioritiesAsync(client, projectA.ProjectId);
        var pause = await client.PostStatusAsync(created.GoalId, "paused");
        pause.EnsureSuccessStatusCode();
        var before = await GetGoalAsync(client, created.GoalId);

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(before.Revision, 7));

        response.EnsureSuccessStatusCode();
        var updated = await response.ReadEditedGoalAsync();
        var fresh = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(updated.Revision, fresh.Revision);
        Assert.True(fresh.Revision > before.Revision);
        Assert.Equal(7, fresh.Config.Rank!.End);
        Assert.Equal(before.Config.Rank!.Start, fresh.Config.Rank.Start); // baseline preserved
        Assert.Equal(created.GoalId, fresh.GoalId);
        Assert.Equal("Paused", fresh.Status); // lifecycle status untouched
        Assert.Equal(before.ProjectIds.Order(), fresh.ProjectIds.Order());
        Assert.Equal(before.Snapshot!.InitialRank, fresh.Snapshot!.InitialRank);
        Assert.Equal(orderBefore, await PrioritiesAsync(client, projectA.ProjectId));
        Assert.Equal(before.CreatedAt, fresh.CreatedAt);
        Assert.NotEqual(other.GoalId, fresh.GoalId);
    }

    [Fact]
    public async Task NonTargetFieldsAreUnchangedByATargetEdit()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client,
            RankGoal with { Config = RankGoal.Config with { FarmingStrategy = "EveryStep" } });
        var notes = await client.PutDetailsAsync(created.GoalId, new UpdateGoalRequest("keep me", null, "EveryStep"));
        notes.EnsureSuccessStatusCode();
        var before = await GetGoalAsync(client, created.GoalId);

        (await PutTargetAsync(client, created.GoalId, RankEdit(before.Revision, 6, applied: 2)))
            .EnsureSuccessStatusCode();

        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal("keep me", after.Notes);
        Assert.Equal("EveryStep", after.Config.FarmingStrategy);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(6, after.Config.Rank!.End);
        Assert.Equal(2, after.Config.Rank.EndAppliedUpgrades);
    }

    [Fact]
    public async Task AscensionTargetCanBeEdited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client,
            Goal("ascension", new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(
                created.Revision, new GoalTargetEditRequest(Progression: new ProgressionEndTargetRequest("Common:TwoStars"))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal("Common:TwoStars", updated.Config.Progression!.End);
        Assert.Equal("Common:None", updated.Config.Progression.Start);
    }

    [Fact]
    public async Task AbilityTargetCanBeEdited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client, Goal("ability", new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(created.Revision, new GoalTargetEditRequest(Ability: new AbilityEndTargetRequest(5, 4))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(5, updated.Config.Ability!.ActiveEnd);
        Assert.Equal(4, updated.Config.Ability.PassiveEnd);
        Assert.Equal(0, updated.Config.Ability.ActiveStart);
    }

    [Fact]
    public async Task UpgradeTargetCanBeEdited()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 2)]))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(
                created.Revision,
                new GoalTargetEditRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 9)]))));

        response.EnsureSuccessStatusCode();
        var updated = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(9, Assert.Single(updated.Config.Upgrade!.Targets).Quantity);
        Assert.Equal(created.Revision + 1, updated.Revision);
    }

    [Fact]
    public async Task UpgradeRangeCanBeReplacedAndCleared()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var targets = new List<UpgradeMaterialTargetRequest> { new(RelevantUpgradeId, 2) };
        var created = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest(targets, RankRange: new UpgradeRangeRequest(1, 3)))));

        var replace = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(
                created.Revision,
                new GoalTargetEditRequest(Upgrade: new UpgradeTargetRequest(targets, RankRange: new UpgradeRangeRequest(1, 4)))));
        replace.EnsureSuccessStatusCode();
        var replaced = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(new UpgradeRangeResponse(1, 4), replaced.Config.Upgrade!.RankRange);
        Assert.Equal(created.Revision + 1, replaced.Revision);

        var clear = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(replaced.Revision, new GoalTargetEditRequest(Upgrade: new UpgradeTargetRequest(targets))));
        clear.EnsureSuccessStatusCode();
        var cleared = await GetGoalAsync(client, created.GoalId);
        Assert.Null(cleared.Config.Upgrade!.RankRange);
        Assert.Null(cleared.Config.Upgrade.ActiveRange);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(-1, 2)]
    [InlineData(0, 999)]
    public async Task InvalidUpgradeRangeIsRejectedOnEditAndNothingChanges(int start, int end)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var targets = new List<UpgradeMaterialTargetRequest> { new(RelevantUpgradeId, 2) };
        var created = await CreateGoalAsync(
            client, Goal("upgrade", new CreateGoalConfigRequest(Upgrade: new UpgradeTargetRequest(targets))));

        var response = await PutTargetAsync(
            client,
            created.GoalId,
            new UpdateGoalTargetRequest(
                created.Revision,
                new GoalTargetEditRequest(Upgrade: new UpgradeTargetRequest(targets, RankRange: new UpgradeRangeRequest(start, end)))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(created.Revision, after.Revision);
        Assert.Null(after.Config.Upgrade!.RankRange);
    }

    [Fact]
    public async Task UnlockGoalHasNoAdjustableTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, Goal("unlock", new CreateGoalConfigRequest()));

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, 7));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(created.Revision, (await GetGoalAsync(client, created.GoalId)).Revision);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("archived")]
    public async Task HistoricalGoalTargetCannotBeEdited(string status)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        (await client.PostStatusAsync(created.GoalId, status)).EnsureSuccessStatusCode();
        var before = await GetGoalAsync(client, created.GoalId);

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(before.Revision, 7));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(5, (await GetGoalAsync(client, created.GoalId)).Config.Rank!.End);
    }

    [Fact]
    public async Task MalformedOrMismatchedTargetGroupsAreRejectedWithoutChange()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);

        UpdateGoalTargetRequest[] bad =
        [
            new(created.Revision, new GoalTargetEditRequest()), // no group at all
            new(created.Revision, new GoalTargetEditRequest(Ability: new AbilityEndTargetRequest(5, 0))), // wrong kind
            new(created.Revision, new GoalTargetEditRequest( // mixed groups
                Rank: new RankEndTargetRequest(7, false, 0), Ability: new AbilityEndTargetRequest(5, 0))),
        ];
        foreach (var request in bad)
        {
            var response = await PutTargetAsync(client, created.GoalId, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(created.Revision, after.Revision);
        Assert.Equal(5, after.Config.Rank!.End);
    }

    [Theory]
    [InlineData(1)] // not above the stored baseline start
    [InlineData(0)]
    [InlineData(999)] // beyond the rank ladder
    public async Task InvalidRankTargetsAreRejectedWithCreationRules(int end)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);

        var response = await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, end));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(created.Revision, (await GetGoalAsync(client, created.GoalId)).Revision);
    }

    [Fact]
    public async Task InvalidUpgradeTargetsAreRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var upgrade = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 2)]))));

        var irrelevant = await PutTargetAsync(
            client,
            upgrade.GoalId,
            new UpdateGoalTargetRequest(
                upgrade.Revision,
                new GoalTargetEditRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(UnrelatedUpgradeId, 1)]))));

        Assert.Equal(HttpStatusCode.BadRequest, irrelevant.StatusCode);
    }

    [Fact]
    public async Task AlreadyReachedTargetIsSavedWithoutChangingStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject: "target-edit-reached");
        var created = await CreateGoalAsync(
            client, Goal("rank", new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 12, false, 0))));
        await SeedProgressAsync(
            "target-edit-reached",
            new PlayerCharacterRecord { UnitId = UnitId.From(CharacterId), Rank = (UnitRank)8, XpLevel = 60 });

        // The unit is now at rank 8, past the new target of 5 — a valid target relative to the baseline.
        var response = await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, 5));

        response.EnsureSuccessStatusCode();
        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(5, after.Config.Rank!.End);
        Assert.Equal("Active", after.Status);
    }

    [Fact]
    public async Task RankTargetHeldByAnotherInFlightGoalIsAnAtomicConflict()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var shared = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Projects = [new ProjectPriorityRequest(projectA.ProjectId), new ProjectPriorityRequest(projectB.ProjectId)],
            });
        var occupant = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 7, false, 0)),
                Projects = [new ProjectPriorityRequest(projectB.ProjectId)],
            });

        var response = await PutTargetAsync(client, shared.GoalId, RankEdit(shared.Revision, 7));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.ReadProblemAsync();
        Assert.Equal("slotOccupied", conflict["issueCode"]!.GetValue<string>());
        Assert.Equal(occupant.GoalId, conflict["existingGoalId"]!.GetValue<Guid>());
        Assert.Equal("7:0", conflict["normalizedTarget"]!.GetValue<string>());
        Assert.Equal("Rank", conflict["goalType"]!.GetValue<string>());
        Assert.Null(conflict["conflicts"]);
        var unchanged = await GetGoalAsync(client, shared.GoalId);
        Assert.Equal(5, unchanged.Config.Rank!.End);
        Assert.Equal(shared.Revision, unchanged.Revision);
    }

    [Fact]
    public async Task ARankTargetHeldOutsideTheGoalsProjectsStillConflicts()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var goal = await CreateGoalAsync(client, RankGoal with { Projects = [new ProjectPriorityRequest(projectA.ProjectId)] });
        var holder = await CreateGoalAsync(
            client,
            RankGoal with
            {
                Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 7, false, 0)),
                Projects = [new ProjectPriorityRequest(projectB.ProjectId)],
            });

        var response = await PutTargetAsync(client, goal.GoalId, RankEdit(goal.Revision, 7));

        // Slots are per profile, not per project.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(holder.GoalId, (await response.ReadCurrentAsync<GoalDetailResponse>()).GoalId);
    }

    [Fact]
    public async Task MissingUpgradeListOrBlankIdsAreABadRequestNotAServerError()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var upgrade = await CreateGoalAsync(
            client,
            Goal("upgrade", new CreateGoalConfigRequest(
                Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(RelevantUpgradeId, 2)]))));
        var ascension = await CreateGoalAsync(
            client,
            Goal("ascension", new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"))));

        var noList = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{upgrade.GoalId}",
            new { target = new { expectedRevision = upgrade.Revision, target = new { upgrade = new { targets = (object?)null } } } },
            TestContext.Current.CancellationToken);
        var blankId = await PutTargetAsync(
            client,
            upgrade.GoalId,
            new UpdateGoalTargetRequest(
                upgrade.Revision,
                new GoalTargetEditRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest("  ", 1)]))));
        var noStep = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{ascension.GoalId}",
            new { target = new { expectedRevision = ascension.Revision, target = new { progression = new { end = (string?)null } } } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noStep.StatusCode);
    }

    [Fact]
    public async Task EditedRankTargetFreesTheOldSlotAndOccupiesTheNewOne()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        (await PutTargetAsync(client, created.GoalId, RankEdit(created.Revision, 7))).EnsureSuccessStatusCode();

        // The old target (5) is free again; the new target (7) is now held.
        await CreateGoalAsync(client, RankGoal);
        var heldNow = await client.PostGoalAsync(
            RankGoal with { Config = new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 7, false, 0)) });
        Assert.Equal(HttpStatusCode.Conflict, heldNow.StatusCode);
    }

    [Fact]
    public async Task StaleRevisionIsRejectedWithTheCurrentGoalAndNoOverwrite()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        var loadedRevision = created.Revision;

        (await PutTargetAsync(client, created.GoalId, RankEdit(loadedRevision, 7))).EnsureSuccessStatusCode();
        var current = await GetGoalAsync(client, created.GoalId);

        var stale = await PutTargetAsync(client, created.GoalId, RankEdit(loadedRevision, 6));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("goalRevisionStale", await stale.ReadIssueCodeAsync());
        var conflict = await stale.ReadCurrentAsync<GoalDetailResponse>();
        Assert.Equal(current.Revision, conflict.Revision);
        Assert.Equal(7, conflict.Config.Rank!.End);
        var after = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(7, after.Config.Rank!.End);
        Assert.Equal(current.Revision, after.Revision);
    }

    [Fact]
    public async Task RetryingAnIdenticalTargetLeavesTheRevisionUnchanged()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        var request = RankEdit(created.Revision, 7);

        (await PutTargetAsync(client, created.GoalId, request)).EnsureSuccessStatusCode();
        var afterFirst = await GetGoalAsync(client, created.GoalId);
        var retry = await PutTargetAsync(client, created.GoalId, request); // same (now stale) revision

        retry.EnsureSuccessStatusCode();
        var afterRetry = await GetGoalAsync(client, created.GoalId);
        Assert.Equal(afterFirst.Revision, afterRetry.Revision);
        Assert.Equal(created.Revision + 1, afterRetry.Revision);
    }

    [Fact]
    public async Task GoalDetailExposesAMonotonicRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client, RankGoal);
        Assert.True(created.Revision > 0);

        var paused = await client.PostStatusAsync(created.GoalId, "paused");
        paused.EnsureSuccessStatusCode();

        Assert.True((await GetGoalAsync(client, created.GoalId)).Revision > created.Revision);
    }

    [Fact]
    public async Task OtherUsersGoalIsNotFound()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(owner, RankGoal);
        var stranger = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await PutTargetAsync(stranger, created.GoalId, RankEdit(created.Revision, 7));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SeedProgressAsync(string subject, PlayerCharacterRecord character)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profile = await db.Profiles.IgnoreQueryFilters()
            .FirstAsync(entity => entity.Subject == subject, TestContext.Current.CancellationToken);
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot { Id = profile.Id, Characters = [character] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PutTargetAsync(HttpClient client, Guid goalId, UpdateGoalTargetRequest request) =>
        client.PutTargetAsync(goalId, request);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        return body;
    }

    private static Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request) =>
        client.CreateGoalAsync(request);

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId)
    {
        var goal = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }

    private static async Task<List<(Guid GoalId, int? Priority)>> PrioritiesAsync(HttpClient client, Guid projectId)
    {
        var goals = (await client.ListGoalsAsync()).Goals.ToDictionary(goal => goal.GoalId);
        return (await client.ProjectGoalIdsAsync(projectId)).Select(id => (id, goals[id].GlobalPriority)).ToList();
    }

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest(name, null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<ProjectSummaryResponse>(response);
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
