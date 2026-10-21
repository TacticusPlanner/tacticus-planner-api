using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>goal-collection-read</c>: the goals list (full detail with <c>projectIds</c>) and the projects list
/// (<c>goalIds</c>) are together the whole planning read model, under one archived selector.</summary>
public sealed class GoalCollectionReadTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateGoalRequest Rank(int end, params Guid[] projectIds) => new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        projectIds.Length == 0 ? null : projectIds.Select(id => new ProjectPriorityRequest(id)).ToList());

    [Fact]
    public async Task AListEntryEqualsTheGoalDetail()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var a = await DefaultProjectIdAsync(client);
        var b = await CreateProjectAsync(client, "B");
        var goal = await client.CreateGoalAsync(Rank(5, a, b) with
        {
            Snapshot = new CreateGoalSnapshotRequest(InitialRank: "Stone1"),
        });

        var listed = Assert.Single((await client.ListGoalsAsync()).Goals);
        var detail = await client.GetFromJsonAsync<GoalDetailResponse>($"/api/v1/me/goals/{goal.GoalId}", Ct);

        Assert.Equivalent(detail, listed, strict: true);
        Assert.NotNull(listed.Config.Rank);
        Assert.Equal(new[] { a, b }.Order(), listed.ProjectIds);
        Assert.DoesNotContain("\"events\"", await client.GetStringAsync("/api/v1/me/goals", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InFlightGoalsComeFirstInOrderThenTheRestNewestFirst()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var goals = new List<GoalDetailResponse>();
        for (var end = 3; end <= 7; end++)
            goals.Add(await client.CreateGoalAsync(Rank(end)));
        (await client.PostStatusAsync("Completed", null, goals[1].GoalId)).EnsureSuccessStatusCode();
        (await client.PostStatusAsync("Completed", null, goals[3].GoalId)).EnsureSuccessStatusCode();

        var list = await client.ListGoalsAsync();

        Assert.Equal(
            [goals[0].GoalId, goals[2].GoalId, goals[4].GoalId, goals[3].GoalId, goals[1].GoalId],
            list.Goals.Select(goal => goal.GoalId));
        Assert.Equal([1, 2, 3, null, null], list.Goals.Select(goal => goal.GlobalPriority));
    }

    [Fact]
    public async Task AProjectListsItsMembersInGlobalOrder()
    {
        // Project A holds X (position 2), Y (position 1) and a Completed Z: goalIds [Y, X, Z].
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var a = await CreateProjectAsync(client, "A");
        var y = await client.CreateGoalAsync(Rank(3, a));
        var x = await client.CreateGoalAsync(Rank(4, a));
        var z = await client.CreateGoalAsync(Rank(5, a));
        (await client.PostStatusAsync("Completed", null, z.GoalId)).EnsureSuccessStatusCode();

        Assert.Equal([y.GoalId, x.GoalId, z.GoalId], await client.ProjectGoalIdsAsync(a));
    }

    [Fact]
    public async Task BothListsUseTheSameArchivedSelectorAndAgreeOnMembership()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var p = await CreateProjectAsync(client, "P");
        var empty = await CreateProjectAsync(client, "Empty");
        var active = await client.CreateGoalAsync(Rank(3, p));
        var archived = await client.CreateGoalAsync(Rank(4, p));
        (await client.PostStatusAsync("Archived", null, archived.GoalId)).EnsureSuccessStatusCode();

        foreach (var (selector, expected) in new[] { (false, active.GoalId), (true, archived.GoalId) })
        {
            var goals = (await client.ListGoalsAsync(selector)).Goals;
            var projects = (await client.GetFromJsonAsync<ListProjectsResponse>(
                $"/api/v1/me/projects{(selector ? "?archived=true" : "")}", Ct))!.Projects;

            Assert.Equal([expected], goals.Select(goal => goal.GoalId));
            Assert.Equal([expected], projects.Single(project => project.ProjectId == p).GoalIds);
            // The selector filters goalIds, never projects.
            Assert.Empty(projects.Single(project => project.ProjectId == empty).GoalIds);
            Assert.Equal(3, projects.Count);
            // Goal G lists P exactly when P lists G, and counts agree.
            foreach (var project in projects)
            {
                Assert.Equal(
                    goals.Where(goal => goal.ProjectIds.Contains(project.ProjectId)).Select(goal => goal.GoalId).Order(),
                    project.GoalIds.Order());
            }
        }
    }

    [Fact]
    public async Task DeletingAPrerequisiteUnlinksItFromItsDependents()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await (await client.PostGoalAsync(new CreateCombinedGoalsRequest(
            "character",
            "blackTerminator",
            null,
            [
                new CombinedGoalSpec("unlock", new CreateGoalConfigRequest(), []),
                new CombinedGoalSpec("ascension",
                    new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar")), [0]),
            ]))).Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(Ct);
        var (unlock, ascension) = (created!.Goals[0], created.Goals[1]);
        Assert.Equal([unlock.GoalId], ascension.DependsOn);

        (await client.DeleteAsync($"/api/v1/me/goals/{unlock.GoalId}", Ct)).EnsureSuccessStatusCode();

        var dependent = await client.GetFromJsonAsync<GoalDetailResponse>($"/api/v1/me/goals/{ascension.GoalId}", Ct);
        Assert.Empty(dependent!.DependsOn);
    }

    [Fact]
    public async Task ARemovedRouteIsNotServed()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await DefaultProjectIdAsync(client);

        var response = await client.GetAsync($"/api/v1/me/projects/{project}/goals", Ct);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    private static async Task<Guid> DefaultProjectIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListProjectsResponse>("/api/v1/me/projects", Ct))!.Projects
            .Single(project => project.IsDefault).ProjectId;

    private static async Task<Guid> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/projects", new CreateProjectRequest(name, null, null), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(Ct))!.ProjectId;
    }
}
