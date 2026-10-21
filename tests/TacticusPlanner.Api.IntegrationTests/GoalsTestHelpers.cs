using System.Net.Http.Json;
using System.Text.Json.Nodes;
using TacticusPlanner.Api.Features.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Shared setup for Goals/Projects endpoint tests: provisions an authenticated client (the profile these
/// endpoints require) and wraps the goal routes in the shapes most tests think in — one goal created, one goal's
/// status changed, one section edited.</summary>
internal static class GoalsTestHelpers
{
    public static async Task<HttpClient> CreateProvisionedClientAsync(PlannerApiFactory factory, string? subject = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            PlannerTestAuthenticationHandler.SubjectHeader,
            subject ?? $"goals-{Guid.NewGuid()}"
        );

        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        return client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Creates one goal: a one-spec <c>POST me/goals</c>.</summary>
    public static Task<HttpResponseMessage> PostGoalAsync(this HttpClient client, CreateGoalRequest request) =>
        client.PostAsJsonAsync("/api/v1/me/goals", request.ToCombined(), Ct);

    /// <summary><c>POST me/goals</c> with a combined (multi-spec) request.</summary>
    public static Task<HttpResponseMessage> PostGoalAsync(this HttpClient client, CreateCombinedGoalsRequest request) =>
        client.PostAsJsonAsync("/api/v1/me/goals", request, Ct);

    /// <summary>The single goal a one-spec creation returned.</summary>
    public static async Task<GoalDetailResponse> ReadCreatedGoalAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(Ct))!.Goals.Single();
    }

    public static async Task<GoalDetailResponse> CreateGoalAsync(this HttpClient client, CreateGoalRequest request) =>
        await (await client.PostGoalAsync(request)).ReadCreatedGoalAsync();

    public static async Task<ListGoalsResponse> ListGoalsAsync(this HttpClient client, bool archived = false) =>
        (await client.GetFromJsonAsync<ListGoalsResponse>($"/api/v1/me/goals{(archived ? "?archived=true" : "")}", Ct))!;

    /// <summary>Transitions goals with <c>POST me/goals/status</c>, reading the current order revision first unless
    /// one is given.</summary>
    public static async Task<HttpResponseMessage> PostStatusAsync(
        this HttpClient client, string status, long? expectedOrderRevision, params Guid[] goalIds)
    {
        expectedOrderRevision ??= (await client.ListGoalsAsync()).OrderRevision;
        return await client.PostAsJsonAsync(
            "/api/v1/me/goals/status", new UpdateGoalsStatusRequest([.. goalIds], status, expectedOrderRevision), Ct);
    }

    public static Task<HttpResponseMessage> PostStatusAsync(this HttpClient client, Guid goalId, string status) =>
        client.PostStatusAsync(status, null, goalId);

    /// <summary>The one goal a status change answered with.</summary>
    public static async Task<GoalDetailResponse> ReadStatusGoalAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UpdateGoalsStatusResponse>(Ct))!.Goals.Single();
    }

    /// <summary><c>PUT me/goals/{goalId}</c> with any combination of sections.</summary>
    public static Task<HttpResponseMessage> EditGoalAsync(this HttpClient client, Guid goalId, EditGoalRequest request) =>
        client.PutAsJsonAsync($"/api/v1/me/goals/{goalId}", request, Ct);

    public static Task<HttpResponseMessage> PutTargetAsync(this HttpClient client, Guid goalId, UpdateGoalTargetRequest target) =>
        client.EditGoalAsync(goalId, new EditGoalRequest(Target: target));

    public static Task<HttpResponseMessage> PutDetailsAsync(this HttpClient client, Guid goalId, UpdateGoalRequest details) =>
        client.EditGoalAsync(goalId, new EditGoalRequest(Details: details));

    public static Task<HttpResponseMessage> PutProjectsAsync(this HttpClient client, Guid goalId, List<Guid> projectIds) =>
        client.EditGoalAsync(goalId, new EditGoalRequest(ProjectIds: projectIds));

    /// <summary>The goal an edit answered with.</summary>
    public static async Task<GoalDetailResponse> ReadEditedGoalAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EditGoalResponse>(Ct))!.Goal;
    }

    /// <summary>A project's member goal ids from the projects list (global order, the given archived selector).</summary>
    public static async Task<List<Guid>> ProjectGoalIdsAsync(this HttpClient client, Guid projectId, bool archived = false)
    {
        var projects = await client.GetFromJsonAsync<Features.Projects.ListProjectsResponse>(
            $"/api/v1/me/projects{(archived ? "?archived=true" : "")}", Ct);
        return projects!.Projects.Single(project => project.ProjectId == projectId).GoalIds;
    }

    /// <summary>A project's member goals in global order, composed from the two lists (the per-project read is
    /// gone).</summary>
    public static async Task<ListProjectGoalsResponse> GetProjectGoalsAsync(this HttpClient client, Guid projectId)
    {
        var goals = (await client.ListGoalsAsync()).Goals.ToDictionary(goal => goal.GoalId);
        return new((await client.ProjectGoalIdsAsync(projectId)).Select(id => new ProjectGoalEntry(goals[id])).ToList());
    }

    /// <summary>The problem-details body of an error response.</summary>
    public static async Task<JsonObject> ReadProblemAsync(this HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
    }

    public static async Task<string?> ReadIssueCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadProblemAsync())["issueCode"]?.GetValue<string>();

    /// <summary>The problem's <c>current</c> resource, read as <typeparamref name="T"/>.</summary>
    public static async Task<T> ReadCurrentAsync<T>(this HttpResponseMessage response) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(
            (await response.ReadProblemAsync())["current"], System.Text.Json.JsonSerializerOptions.Web)!;
}

/// <summary>The single-goal creation shape, test-side: <c>POST me/goals</c> takes the combined request, so this
/// becomes a one-spec request (<see cref="ToCombined"/>).</summary>
public sealed record CreateGoalRequest(
    string EntityType,
    string EntityId,
    string GoalType,
    CreateGoalConfigRequest Config,
    List<ProjectPriorityRequest>? Projects,
    CreateGoalSnapshotRequest? Snapshot = null,
    bool StartPaused = false)
{
    public CreateCombinedGoalsRequest ToCombined() =>
        new(EntityType, EntityId, Projects, [new CombinedGoalSpec(GoalType, Config, [], Snapshot)], StartPaused);
}

public sealed record ListProjectGoalsResponse(List<ProjectGoalEntry> Goals);

public sealed record ProjectGoalEntry(GoalDetailResponse Goal);
