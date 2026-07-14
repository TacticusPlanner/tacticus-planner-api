using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

public sealed class CreateCombinedGoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateCombinedGoalsRequest UnlockThenRank = new(
        "character",
        "unit-1",
        null,
        [
            new CombinedGoalSpec("unlock", new CreateGoalConfigRequest(), []),
            new CombinedGoalSpec(
                "rank",
                new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, 15, false, 0)),
                [0]
            ),
        ]
    );

    [Fact]
    public async Task CreatesLinkedGoalsWithSharedAggregateIdAndDependsOnEdge()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            UnlockThenRank,
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal(2, created.Goals.Count);
        var unlock = created.Goals[0];
        var rank = created.Goals[1];

        Assert.Equal("Unlock", unlock.GoalType);
        Assert.Equal("Rank", rank.GoalType);

        Assert.NotNull(unlock.AggregateId);
        Assert.Equal(unlock.AggregateId, rank.AggregateId);

        Assert.Empty(unlock.DependsOn);
        Assert.Equal([unlock.GoalId], rank.DependsOn);

        Assert.Empty(unlock.Milestones);
        Assert.NotEmpty(rank.Milestones);
        Assert.Equal("Diamond1", rank.Milestones[^1].TargetState);

        var defaultProject = await GetDefaultProjectAsync(client);
        var membersResponse = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(membersResponse);
        var unlockMember = membersResponse.Goals.Single(entry => entry.Goal.GoalId == unlock.GoalId);
        var rankMember = membersResponse.Goals.Single(entry => entry.Goal.GoalId == rank.GoalId);
        Assert.NotEqual(unlockMember.Priority, rankMember.Priority);
    }

    [Fact]
    public async Task CreateInNonActiveProjectStartsAllGoalsPaused()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            UnlockThenRank with { ProjectId = otherProject.ProjectId },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.All(created.Goals, goal => Assert.Equal("Paused", goal.Status));
    }

    [Fact]
    public async Task UnknownProjectIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            UnlockThenRank with { ProjectId = Guid.NewGuid() },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForwardDependsOnIndexIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var request = UnlockThenRank with
        {
            Goals =
            [
                new CombinedGoalSpec("unlock", new CreateGoalConfigRequest(), [1]), // forward reference
                new CombinedGoalSpec(
                    "rank",
                    new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, 15, false, 0)),
                    []
                ),
            ],
        };

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyGoalsListIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            UnlockThenRank with { Goals = [] },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeferredGoalTypeIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var request = UnlockThenRank with
        {
            Goals = [new CombinedGoalSpec("material", new CreateGoalConfigRequest(), [])],
        };

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MowRequestWithARankGoalIsRejected()
    {
        // Machines of War have no rank ladder (plan §16 phase 6) — rejected even when Rank isn't the
        // only spec in the request.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var request = UnlockThenRank with { EntityType = "mow", EntityId = "mow-1" };

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MowUnlockThenAbilityGoalIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var request = new CreateCombinedGoalsRequest(
            "mow",
            "mow-1",
            null,
            [
                new CombinedGoalSpec("unlock", new CreateGoalConfigRequest(), []),
                new CombinedGoalSpec(
                    "ability",
                    new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)),
                    [0]
                ),
            ]
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            request,
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal(2, created.Goals.Count);
        Assert.All(created.Goals, goal => Assert.Equal("Mow", goal.EntityType));
        Assert.Equal("Ability", created.Goals[1].GoalType);
        Assert.Equal([created.Goals[0].GoalId], created.Goals[1].DependsOn);
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
