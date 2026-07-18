using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

public sealed class CreateCombinedGoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateCombinedGoalsRequest UnlockThenRank = new(
        "character",
        "blackTerminator",
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
    public async Task PersistsExpandedImmutableSnapshotFromCombinedSpec()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var snapshot = new CreateGoalSnapshotRequest(
            InitialRank: "Silver1",
            InitialProgression: "Rare3Stars",
            InitialActiveAbilityLevel: 20,
            InitialPassiveAbilityLevel: 18,
            InitialUnlocked: true,
            InitialRequirement: [new GoalSnapshotResourceRequest("material-1", 30)],
            InitialInventoryContribution: [new GoalSnapshotResourceRequest("material-1", 5)],
            OriginalEnergyTotal: 120,
            OriginalRaidsTotal: 20,
            OriginalEstimateDays: 2,
            OriginalEstimateDate: new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero)
        );
        var request = UnlockThenRank with
        {
            Goals = [UnlockThenRank.Goals[1] with { DependsOnIndex = [], Snapshot = snapshot }],
        };

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            request,
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        var stored = Assert.Single(created!.Goals).Snapshot;
        Assert.NotNull(stored);
        Assert.Equal("Silver1", stored.InitialRank);
        Assert.Equal(30, Assert.Single(stored.InitialRequirement).Count);
        Assert.Equal(5, Assert.Single(stored.InitialInventoryContribution).Count);
        Assert.Equal(120, stored.OriginalEnergyTotal);
        Assert.Equal(2, stored.OriginalEstimateDays);
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
            UnlockThenRank with { ProjectIds = [otherProject.ProjectId] },
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
            UnlockThenRank with { ProjectIds = [Guid.NewGuid()] },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithMultipleProjectIdsAddsEveryGoalToEachProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            UnlockThenRank with { ProjectIds = [defaultProject.ProjectId, otherProject.ProjectId] },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.All(
            created.Goals,
            goal => Assert.Equal(
                new HashSet<Guid> { defaultProject.ProjectId, otherProject.ProjectId },
                goal.ProjectIds.ToHashSet()
            )
        );

        var otherMembers = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{otherProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(otherMembers);
        Assert.Equal(created.Goals.Count, otherMembers.Goals.Count);
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

        var request = UnlockThenRank with { EntityType = "mow", EntityId = "astraOrdnanceBattery" };

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            request,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MowUnlockGoalIsRejectedBecauseUnlockRequiresCharacterShardLocations()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var request = new CreateCombinedGoalsRequest(
            "mow",
            "astraOrdnanceBattery",
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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
