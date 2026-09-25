using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for <c>rank-milestones</c> / <c>project-goal-slots</c> (openspec change
/// support-multiple-rank-milestones): a project can hold several in-flight Rank goals for one unit as long
/// as their normalized end targets differ; an exact duplicate is a structured 409 naming the existing goal.
/// The API tests run on InMemory (no partial unique index), so these exercise the friendly pre-checks; the
/// relational index itself is covered by the Postgres integration tests.</summary>
public sealed class RankMilestonesEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";

    private static CreateGoalRequest RankGoal(int end, bool endPointFive = false, int endAppliedUpgrades = 0) => new(
        "character",
        CharacterId,
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, endPointFive, endAppliedUpgrades)),
        null);

    [Fact]
    public async Task DistinctRankTargetsCoexistInOneProjectAndStayIndependentlyManageable()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var silver = await CreateGoalAsync(client, RankGoal(5));
        var gold = await CreateGoalAsync(client, RankGoal(6));

        Assert.NotEqual(silver.GoalId, gold.GoalId);
        Assert.Equal(silver.ProjectIds, gold.ProjectIds);

        var pause = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{silver.GoalId}/status", new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken);
        pause.EnsureSuccessStatusCode();
        Assert.Equal("Active", (await GetGoalAsync(client, gold.GoalId)).Status);
        Assert.Equal("Paused", (await GetGoalAsync(client, silver.GoalId)).Status);
    }

    [Fact]
    public async Task ExactDuplicateRankTargetIsRejectedNamingTheExistingGoalAndTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var existing = await CreateGoalAsync(client, RankGoal(5));

        // Start and strategy are not part of a target's identity.
        var duplicate = RankGoal(5) with
        {
            Config = new CreateGoalConfigRequest(
                Rank: new RankTargetRequest(2, false, 0, 5, false, 0), FarmingStrategy: "EveryStep"),
        };
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", duplicate, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<ProjectGoalSlotConflictResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(conflict);
        Assert.Equal(existing.GoalId, conflict.ExistingGoalId);
        Assert.Equal(existing.ProjectIds.Single(), conflict.ProjectId);
        Assert.Equal(CharacterId, conflict.EntityId);
        Assert.Equal("Rank", conflict.GoalType);
        Assert.Equal("5:0", conflict.NormalizedTarget);
    }

    [Fact]
    public async Task EquivalentPointFiveAndThreeAppliedSlotsAreTheSameTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var existing = await CreateGoalAsync(client, RankGoal(5, endAppliedUpgrades: 3));

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals", RankGoal(5, endPointFive: true), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<ProjectGoalSlotConflictResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(existing.GoalId, conflict?.ExistingGoalId);
        Assert.Equal("5:3", conflict?.NormalizedTarget);
    }

    [Fact]
    public async Task ClearAndPartialAppliedSlotsOfTheSameRankAreDistinctTargets()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateGoalAsync(client, RankGoal(5));

        await CreateGoalAsync(client, RankGoal(5, endAppliedUpgrades: 2));
    }

    [Fact]
    public async Task CombinedCreationAllowsADistinctTargetAndRejectsAnExactDuplicate()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateGoalAsync(client, RankGoal(5));

        var distinct = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined", CombinedRank(6), TestContext.Current.CancellationToken);
        distinct.EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined", CombinedRank(5), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task AddingAGoalToAProjectHoldingItsTargetFailsAtomicallyAndNamesThatProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");

        var goal = await CreateGoalAsync(client, RankGoal(5));
        var occupant = await CreateGoalAsync(
            client, RankGoal(5) with { Projects = [new ProjectPriorityRequest(projectB.ProjectId)] });

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{goal.GoalId}/projects",
            new UpdateGoalProjectsRequest([projectA.ProjectId, projectB.ProjectId]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<ProjectGoalSlotConflictResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal(projectB.ProjectId, conflict?.ProjectId);
        Assert.Equal(occupant.GoalId, conflict?.ExistingGoalId);
        Assert.Equal([projectA.ProjectId], (await GetGoalAsync(client, goal.GoalId)).ProjectIds);
    }

    [Fact]
    public async Task AddingADistinctTargetToAProjectHoldingAnotherMilestoneSucceeds()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");

        var goal = await CreateGoalAsync(client, RankGoal(5));
        await CreateGoalAsync(client, RankGoal(6) with { Projects = [new ProjectPriorityRequest(projectB.ProjectId)] });

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{goal.GoalId}/projects",
            new UpdateGoalProjectsRequest([projectA.ProjectId, projectB.ProjectId]),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ProjectMembershipReplacementRejectsTwoGoalsWithTheSameTargetAndAcceptsDistinctOnes()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var silver = await CreateGoalAsync(client, RankGoal(5));
        var gold = await CreateGoalAsync(client, RankGoal(6));
        var otherSilver = await CreateGoalAsync(
            client, RankGoal(5) with { Projects = [new ProjectPriorityRequest(projectB.ProjectId)] });

        var distinct = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{projectB.ProjectId}/goals",
            new UpdateProjectGoalsRequest([
                new ProjectGoalEntryRequest(otherSilver.GoalId), new ProjectGoalEntryRequest(gold.GoalId)]),
            TestContext.Current.CancellationToken);
        distinct.EnsureSuccessStatusCode();

        var duplicate = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{projectB.ProjectId}/goals",
            new UpdateProjectGoalsRequest([
                new ProjectGoalEntryRequest(otherSilver.GoalId),
                new ProjectGoalEntryRequest(gold.GoalId),
                new ProjectGoalEntryRequest(silver.GoalId)]),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var conflict = await duplicate.Content.ReadFromJsonAsync<ProjectGoalSlotConflictResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal("5:0", conflict?.NormalizedTarget);
        Assert.Equal(projectB.ProjectId, conflict?.ProjectId);
        Assert.DoesNotContain(projectB.ProjectId, (await GetGoalAsync(client, silver.GoalId)).ProjectIds);
        Assert.Contains(projectA.ProjectId, (await GetGoalAsync(client, silver.GoalId)).ProjectIds);
    }

    [Fact]
    public async Task ResumingAnArchivedRankGoalConflictsOnlyWithTheSameTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var archived = await CreateGoalAsync(client, RankGoal(5));
        var archive = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{archived.GoalId}/status", new UpdateGoalStatusRequest("archived"),
            TestContext.Current.CancellationToken);
        archive.EnsureSuccessStatusCode();

        // A historical goal does not hold its target, so a new in-flight goal can take it...
        var taker = await CreateGoalAsync(client, RankGoal(5));
        var blocked = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{archived.GoalId}/status", new UpdateGoalStatusRequest("active"),
            TestContext.Current.CancellationToken);
        // ...and the historical one can no longer re-enter while the taker holds it.
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(
            taker.GoalId,
            (await blocked.Content.ReadFromJsonAsync<ProjectGoalSlotConflictResponse>(
                TestContext.Current.CancellationToken))?.ExistingGoalId);

        // A different target for the same unit never blocks the resume.
        var other = await CreateGoalAsync(client, RankGoal(7));
        var archiveOther = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{other.GoalId}/status", new UpdateGoalStatusRequest("archived"),
            TestContext.Current.CancellationToken);
        archiveOther.EnsureSuccessStatusCode();
        var resumeOther = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{other.GoalId}/status", new UpdateGoalStatusRequest("active"),
            TestContext.Current.CancellationToken);
        resumeOther.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CompletedMilestoneDoesNotBlockANewGoalAtTheSameTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var completed = await CreateGoalAsync(client, RankGoal(5));
        var complete = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{completed.GoalId}/status", new UpdateGoalStatusRequest("completed"),
            TestContext.Current.CancellationToken);
        complete.EnsureSuccessStatusCode();

        var recreated = await CreateGoalAsync(client, RankGoal(5));

        Assert.NotEqual(completed.GoalId, recreated.GoalId);
        Assert.Equal("Completed", (await GetGoalAsync(client, completed.GoalId)).Status);
    }

    private static CreateCombinedGoalsRequest CombinedRank(int end) => new(
        "character",
        CharacterId,
        null,
        [new CombinedGoalSpec(
            "rank",
            new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
            [])]);

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId)
    {
        var goal = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }

    private static async Task<ProjectSummaryResponse> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/projects", new CreateProjectRequest(name, null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(project);
        return project;
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
