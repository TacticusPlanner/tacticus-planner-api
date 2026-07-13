using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;

namespace TacticusPlanner.Api.Tests;

public sealed class GoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "unit-1",
        "rank",
        new CreateGoalConfigRequest(RankStart: 1, RankEnd: 5),
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
        Assert.Equal("created", created.Events[0].Type);

        var listResponse = await client.GetAsync("/api/v1/me/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Contains(list.Goals, goal => goal.GoalId == created.GoalId);
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
    [InlineData("upgrade", "rank")]
    [InlineData("character", "material")]
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
    public async Task UpdateGoalChangesNotesAndFarmingOverride()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            new UpdateGoalRequest("remember to farm epic first", "manual", ["node-1"]),
            TestContext.Current.CancellationToken
        );
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal("remember to farm epic first", updated.Notes);
        Assert.Equal("manual", updated.Config.FarmingMode);
        Assert.Equal(["node-1"], updated.Config.FarmingLocationIds);
        // The immutable target fields must be untouched by an edit.
        Assert.Equal(created.Config.RankStart, updated.Config.RankStart);
        Assert.Equal(created.Config.RankEnd, updated.Config.RankEnd);
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
        Assert.Contains(paused.Events, evt => evt.Type == "paused");
    }

    [Fact]
    public async Task UpdateGoalStatusUnknownStatusIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/me/goals/{created.GoalId}/status",
            new UpdateGoalStatusRequest("deleted"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteGoalSoftDeleteHidesFromListButKeepsRow()
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

        // Soft-deleted, so a second (non-purge) delete of the same goal is a 404, not a no-op success.
        var secondDelete = await client.DeleteAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task DeleteGoalPurgeRemovesRowPermanently()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var purgeResponse = await client.DeleteAsync(
            $"/api/v1/me/goals/{created.GoalId}?purge=true",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, purgeResponse.StatusCode);

        var getResponse = await client.GetAsync(
            $"/api/v1/me/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
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

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/goals", RankGoal, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(goal);
        return goal;
    }
}
