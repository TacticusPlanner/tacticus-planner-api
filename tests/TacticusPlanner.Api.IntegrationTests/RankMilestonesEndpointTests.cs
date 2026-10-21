using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Coverage for <c>rank-milestones</c> / <c>project-goal-slots</c>: a profile can hold several in-flight
/// Rank goals for one unit as long as their normalized end targets differ; an exact duplicate is a 409
/// <c>slotOccupied</c> naming the existing goal, whatever the projects. Membership is never slot-checked. The
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

        var pause = await client.PostStatusAsync(silver.GoalId, "paused");
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
        var response = await client.PostGoalAsync(duplicate);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.ReadProblemAsync();
        Assert.Equal("slotOccupied", conflict["issueCode"]!.GetValue<string>());
        Assert.Equal(existing.GoalId, conflict["existingGoalId"]!.GetValue<Guid>());
        Assert.Equal(CharacterId, conflict["entityId"]!.GetValue<string>());
        Assert.Equal("Character", conflict["entityType"]!.GetValue<string>());
        Assert.Equal("Rank", conflict["goalType"]!.GetValue<string>());
        Assert.Equal("5:0", conflict["normalizedTarget"]!.GetValue<string>());
        Assert.Equal(existing.GoalId, conflict["current"]!["goalId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task EquivalentPointFiveAndThreeAppliedSlotsAreTheSameTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var existing = await CreateGoalAsync(client, RankGoal(5, endAppliedUpgrades: 3));

        var response = await client.PostGoalAsync(RankGoal(5, endPointFive: true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.ReadProblemAsync();
        Assert.Equal(existing.GoalId, conflict["existingGoalId"]!.GetValue<Guid>());
        Assert.Equal("5:3", conflict["normalizedTarget"]!.GetValue<string>());
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
            "/api/v1/me/goals", CombinedRank(6), TestContext.Current.CancellationToken);
        distinct.EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/me/goals", CombinedRank(5), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task AnotherProjectGetsTheExistingGoalNotASecondInstance()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var goal = await CreateGoalAsync(client, RankGoal(5));

        var secondInstance = await client.PostGoalAsync(
            RankGoal(5) with { Projects = [new ProjectPriorityRequest(projectB.ProjectId)] });
        var shared = await client.PutProjectsAsync(goal.GoalId, [projectA.ProjectId, projectB.ProjectId]);

        Assert.Equal(HttpStatusCode.Conflict, secondInstance.StatusCode);
        Assert.Equal(goal.GoalId, (await secondInstance.ReadProblemAsync())["existingGoalId"]!.GetValue<Guid>());
        shared.EnsureSuccessStatusCode();
        Assert.Equal([projectA.ProjectId, projectB.ProjectId], (await shared.ReadEditedGoalAsync()).ProjectIds.Order());
    }

    [Fact]
    public async Task ProjectMembershipReplacementAcceptsSeveralMilestonesOfOneUnit()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var projectA = await GetDefaultProjectAsync(client);
        var projectB = await CreateProjectAsync(client, "Second plan");
        var silver = await CreateGoalAsync(client, RankGoal(5));
        var gold = await CreateGoalAsync(client, RankGoal(6));

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/projects/{projectB.ProjectId}/goals",
            new UpdateProjectGoalsRequest(
                [new ProjectGoalEntryRequest(silver.GoalId), new ProjectGoalEntryRequest(gold.GoalId)], []),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal([silver.GoalId, gold.GoalId], await client.ProjectGoalIdsAsync(projectB.ProjectId));
        Assert.Contains(projectA.ProjectId, (await GetGoalAsync(client, silver.GoalId)).ProjectIds);
    }

    [Fact]
    public async Task ResumingAnArchivedRankGoalConflictsOnlyWithTheSameTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var archived = await CreateGoalAsync(client, RankGoal(5));
        var archive = await client.PostStatusAsync(archived.GoalId, "archived");
        archive.EnsureSuccessStatusCode();

        // A historical goal does not hold its target, so a new in-flight goal can take it...
        var taker = await CreateGoalAsync(client, RankGoal(5));
        var blocked = await client.PostStatusAsync(archived.GoalId, "active");
        // ...and the historical one can no longer re-enter while the taker holds it.
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(taker.GoalId, (await blocked.ReadProblemAsync())["existingGoalId"]!.GetValue<Guid>());

        // A different target for the same unit never blocks the resume.
        var other = await CreateGoalAsync(client, RankGoal(7));
        var archiveOther = await client.PostStatusAsync(other.GoalId, "archived");
        archiveOther.EnsureSuccessStatusCode();
        var resumeOther = await client.PostStatusAsync(other.GoalId, "active");
        resumeOther.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CompletedMilestoneDoesNotBlockANewGoalAtTheSameTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var completed = await CreateGoalAsync(client, RankGoal(5));
        var complete = await client.PostStatusAsync(completed.GoalId, "completed");
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

    private static Task<GoalDetailResponse> CreateGoalAsync(HttpClient client, CreateGoalRequest request) =>
        client.CreateGoalAsync(request);

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
