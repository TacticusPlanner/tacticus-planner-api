using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Tests;

public sealed class GoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    [Fact]
    public async Task UnauthenticatedListIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountListIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"goals-{Guid.NewGuid()}");

        var response = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalThenAppearsInList()
    {
        // GoalsTestHelpers only hits GET /api/v1/me — no ListProjects call — so a goal landing Active in
        // its default project here also proves the default project is seeded during account provisioning,
        // not just lazily by the Projects list endpoint.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal,
            TestContext.Current.CancellationToken
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal("Character", created.EntityType);
        Assert.Equal("Rank", created.GoalType);
        Assert.Equal("Active", created.Status);
        Assert.Single(created.Events);
        Assert.Equal(GoalEventType.Created, created.Events[0].Type);

        var listResponse = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Contains(list.Goals, goal => goal.GoalId == created.GoalId);

        var defaultProject = await GetDefaultProjectAsync(client);
        Assert.Equal([defaultProject.ProjectId], created.ProjectIds);
    }

    [Fact]
    public async Task CreateGoalWithMultipleProjectIdsAddsToEachProject()
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
            "/api/v1/me/goals",
            RankGoal with
            {
                Projects =
                [
                    new ProjectPriorityRequest(defaultProject.ProjectId, null),
                    new ProjectPriorityRequest(otherProject.ProjectId, null),
                ],
            },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal(
            new HashSet<Guid> { defaultProject.ProjectId, otherProject.ProjectId },
            created.ProjectIds.ToHashSet()
        );
        // The default project is the active plan, so the goal starts Active even though the second
        // project isn't — membership in any active-plan project is enough.
        Assert.Equal("Active", created.Status);

        var otherMembers = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{otherProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(otherMembers);
        Assert.Contains(otherMembers.Goals, entry => entry.Goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task CreateGoalWithExplicitPriorityIsHonoredOverAutoAppend()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        // A different goal type for the same character (Level, not Rank) so it doesn't trip the
        // one-active-or-paused-per-(entity,type) constraint — occupies priority 1 in the default
        // project via the normal auto-append path, so the Rank goal's explicit priority below is a
        // deliberate insert-at-the-top, not a coincidence.
        var levelGoalResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "blackTerminator",
                "level",
                new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 10)),
                null
            ),
            TestContext.Current.CancellationToken
        );
        levelGoalResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { Projects = [new ProjectPriorityRequest(defaultProject.ProjectId, 1)] },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        var members = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(members);
        var priority = Assert.Single(members.Goals, entry => entry.Goal.GoalId == created.GoalId).Priority;
        Assert.Equal(1, priority);
    }

    [Fact]
    public async Task CreateGoalWithNonPositivePriorityIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { Projects = [new ProjectPriorityRequest(defaultProject.ProjectId, 0)] },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalInNonActiveProjectStartsPaused()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);
        Assert.False(otherProject.IsActivePlan);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { Projects = [new ProjectPriorityRequest(otherProject.ProjectId, null)] },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Paused", created.Status);
    }

    [Fact]
    public async Task CreateUnlockGoalWithUnavailableFarmingLocationIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "blackTerminator",
                "unlock",
                new CreateGoalConfigRequest(
                    FarmingLocationIds: [CampaignBattleId.From("not-a-real-battle")]
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalWithUnavailableShardBattleIdIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "blackTerminator",
                "ascension",
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"),
                    AscensionFarming: new AscensionFarmingRequest(
                        "Campaign",
                        [CampaignBattleId.From("not-a-real-battle")],
                        []
                    )
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // "deathBlightlord" has a regular-shard campaign node at DGS06 (shards_deathBlightlord) and a
    // mythic-shard node at DGE25 (mythicShards_deathBlightlord) — used below to prove Unlock and
    // Ascension's shard-battle validation actually distinguishes the two types rather than accepting
    // either node for either slot.
    [Fact]
    public async Task CreateUnlockGoalRejectsAMythicOnlyShardLocation()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "deathBlightlord",
                "unlock",
                new CreateGoalConfigRequest(FarmingLocationIds: [CampaignBattleId.From("DGE25")]),
                null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUnlockGoalAcceptsItsOwnRegularShardLocation()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "deathBlightlord",
                "unlock",
                new CreateGoalConfigRequest(FarmingLocationIds: [CampaignBattleId.From("DGS06")]),
                null
            ),
            TestContext.Current.CancellationToken
        );

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateAscensionGoalRejectsAMythicIdInTheRegularShardSlot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "deathBlightlord",
                "ascension",
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"),
                    AscensionFarming: new AscensionFarmingRequest(
                        "Campaign",
                        [CampaignBattleId.From("DGE25")],
                        []
                    )
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalRejectsARegularIdInTheMythicShardSlot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "deathBlightlord",
                "ascension",
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"),
                    AscensionFarming: new AscensionFarmingRequest(
                        "Campaign",
                        [],
                        [CampaignBattleId.From("DGS06")]
                    )
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalAcceptsEachShardBattleIdInItsMatchingSlot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "deathBlightlord",
                "ascension",
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"),
                    AscensionFarming: new AscensionFarmingRequest(
                        "Campaign",
                        [CampaignBattleId.From("DGS06")],
                        [CampaignBattleId.From("DGE25")]
                    )
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateGoalUnknownEntityTypeIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { EntityType = "spaceship" },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0", "rank")]
    [InlineData("999", "rank")]
    [InlineData("character", "0")]
    [InlineData("character", "999")]
    public async Task CreateGoalUndefinedNumericEnumIsRejected(string entityType, string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { EntityType = entityType, GoalType = goalType },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalUndefinedNumericFarmingStrategyIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { Config = RankGoal.Config with { FarmingStrategy = "0" } },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAscensionGoalWithNullBattleCollectionIsRejected(bool nullRegular)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var farming = new AscensionFarmingRequest(
            "Campaign",
            nullRegular ? null! : [],
            nullRegular ? [] : null!
        );

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "blackTerminator",
                "ascension",
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest("Common:None", "Common:OneStar"),
                    AscensionFarming: farming
                ),
                null
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("upgrade", "rank")]
    [InlineData("character", "material")]
    [InlineData("mow", "rank")]
    public async Task CreateGoalDeferredGoalTypeOrEntityTypeIsRejected(string entityType, string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { EntityType = entityType, GoalType = goalType },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMowAbilityGoalIsAccepted()
    {
        // Machines of War have no rank ladder (plan §16 phase 6) — Ability is their natural goal type.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "mow",
                "astraOrdnanceBattery",
                "ability",
                new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)),
                null
            ),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Mow", created.EntityType);
        Assert.Equal("Ability", created.GoalType);
    }

    [Fact]
    public async Task UpdateGoalChangesNotesAndFarmingLocationOverride()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            new UpdateGoalRequest("remember to farm epic first", [CampaignBattleId.From("node-1")]),
            TestContext.Current.CancellationToken
        );
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal("remember to farm epic first", updated.Notes);
        Assert.Equal(["node-1"], updated.Config.FarmingLocationIds);
        // The immutable target fields must be untouched by an edit.
        Assert.NotNull(created.Config.Rank);
        Assert.NotNull(updated.Config.Rank);
        Assert.Equal(created.Config.Rank.Start, updated.Config.Rank.Start);
        Assert.Equal(created.Config.Rank.End, updated.Config.Rank.End);
    }

    [Fact]
    public async Task UpdateGoalStatusTransitionsAndAppendsEvent()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var pauseResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken
        );
        pauseResponse.EnsureSuccessStatusCode();
        var paused = await pauseResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(paused);
        Assert.Equal("Paused", paused.Status);
        Assert.Contains(paused.Events, evt => evt.Type == GoalEventType.Paused);
    }

    [Fact]
    public async Task UpdateGoalStatusUnknownStatusIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("not-a-status"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalStatusUndefinedNumericStatusIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("0"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalRejectsUnsupportedFarmingStrategyForGoalType()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                "blackTerminator",
                "level",
                new CreateGoalConfigRequest(Level: new LevelTargetRequest(1, 10)),
                null
            ),
            TestContext.Current.CancellationToken
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            new UpdateGoalRequest(null, null, "EveryStep"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListGoalsExcludesArchivedByDefaultButArchivedQueryReturnsThem()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var archiveResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("archived"),
            TestContext.Current.CancellationToken
        );
        archiveResponse.EnsureSuccessStatusCode();

        var defaultList = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(defaultList);
        Assert.DoesNotContain(defaultList.Goals, goal => goal.GoalId == created.GoalId);

        var archivedList = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/me/goals?archived=true",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(archivedList);
        Assert.Contains(archivedList.Goals, goal => goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task DeleteGoalRemovesRowPermanently()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Goals, goal => goal.GoalId == created.GoalId);

        var getResponse = await client.GetAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // The row is gone (hard delete), so a second delete is a 404, not a no-op success.
        var secondDelete = await client.DeleteAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task GetGoalOwnedByAnotherProfileIsNotFound()
    {
        var ownerClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(ownerClient);

        var otherClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var response = await otherClient.GetAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalProjectsAddsGoalToAnotherProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var created = await CreateGoalAsync(client); // starts in the default project only

        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/projects",
            new UpdateGoalProjectsRequest([defaultProject.ProjectId, otherProject.ProjectId]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(
            new HashSet<Guid> { defaultProject.ProjectId, otherProject.ProjectId },
            updated.ProjectIds.ToHashSet()
        );

        var otherMembers = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{otherProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(otherMembers);
        Assert.Contains(otherMembers.Goals, entry => entry.Goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task UpdateGoalProjectsRemovesGoalFromProjectKeepingItInAnother()
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

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                Projects =
                [
                    new ProjectPriorityRequest(defaultProject.ProjectId, null),
                    new ProjectPriorityRequest(otherProject.ProjectId, null),
                ],
            },
            TestContext.Current.CancellationToken
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/projects",
            new UpdateGoalProjectsRequest([otherProject.ProjectId]),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal([otherProject.ProjectId], updated.ProjectIds);

        var defaultMembers = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(defaultMembers);
        Assert.DoesNotContain(defaultMembers.Goals, entry => entry.Goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task UpdateGoalProjectsEmptyListIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/projects",
            new UpdateGoalProjectsRequest([]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalProjectsUnknownProjectIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/projects",
            new UpdateGoalProjectsRequest([Guid.NewGuid()]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalProjectsForAnotherProfilesGoalIsNotFound()
    {
        var ownerClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(ownerClient);

        var otherClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var otherDefaultProject = await GetDefaultProjectAsync(otherClient);

        var response = await otherClient.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/projects",
            new UpdateGoalProjectsRequest([otherDefaultProject.ProjectId]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalConflictingWithActiveSameKindGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateGoalAsync(client); // starts Active in the default/active project

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalConflictingWithPausedSameKindGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);
        var pauseResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken
        );
        pauseResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalAllowedWhenSameKindGoalIsCompletedOrArchived()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);
        var completeResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("completed"),
            TestContext.Current.CancellationToken
        );
        completeResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal,
            TestContext.Current.CancellationToken
        );

        response.EnsureSuccessStatusCode();
        var created2 = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created2);
        Assert.NotEqual(created.GoalId, created2.GoalId);
    }

    [Fact]
    public async Task ResumingGoalConflictingWithAnotherActiveSameKindGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await CreateGoalAsync(client); // Active
        var pauseResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{first.GoalId}/status",
            new UpdateGoalStatusRequest("archived"),
            TestContext.Current.CancellationToken
        );
        pauseResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync("/api/v1/me/goals", RankGoal, TestContext.Current.CancellationToken);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(second);

        // Un-archiving the first goal back to Active would exceed the one-active-or-paused-per-kind
        // limit now that the second goal already occupies that slot.
        var resumeResponse = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{first.GoalId}/status",
            new UpdateGoalStatusRequest("active"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, resumeResponse.StatusCode);
    }

    [Fact]
    public async Task PausingAlreadyActiveGoalDoesNotConflictWithItself()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client); // Active

        var response = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("paused"),
            TestContext.Current.CancellationToken
        );

        response.EnsureSuccessStatusCode();
    }

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", RankGoal, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
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
