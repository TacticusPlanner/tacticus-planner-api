using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

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

        await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken);

        return client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Creates one goal: a one-spec <c>POST goals</c>.</summary>
    public static Task<HttpResponseMessage> PostGoalAsync(this HttpClient client, CreateGoalRequest request) =>
        client.PostAsJsonAsync("/api/v1/goals", request.ToCombined(), Ct);

    /// <summary>Sends a raw JSON body: for values the typed (enum) requests can no longer express.</summary>
    public static async Task<HttpResponseMessage> SendRawJsonAsync(
        this HttpClient client, HttpMethod method, string requestUri, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(method, requestUri) { Content = content };
        return await client.SendAsync(request, Ct);
    }

    /// <summary>Sends <paramref name="request"/> as wire JSON after <paramref name="mutate"/> rewrites it — e.g. to
    /// put an unknown enum value into one field.</summary>
    public static Task<HttpResponseMessage> SendMutatedJsonAsync<T>(
        this HttpClient client, HttpMethod method, string requestUri, T request, Action<JsonNode> mutate)
    {
        var node = JsonSerializer.SerializeToNode(request, TestJson.Options)!;
        mutate(node);
        return client.SendRawJsonAsync(method, requestUri, node.ToJsonString());
    }

    /// <summary>One-spec <c>POST goals</c> rewritten by <paramref name="mutate"/> (the combined wire shape:
    /// <c>entityType</c>, <c>goals[0].goalType</c>, <c>goals[0].config</c>, ...).</summary>
    public static Task<HttpResponseMessage> PostMutatedGoalAsync(
        this HttpClient client, CreateGoalRequest request, Action<JsonNode> mutate) =>
        client.SendMutatedJsonAsync(HttpMethod.Post, "/api/v1/goals", request.ToCombined(), mutate);

    /// <summary>A request-binding failure: 400 validation problem with an error keyed by <paramref name="field"/>.</summary>
    public static async Task AssertFieldRejectedAsync(this HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal("validation", problem["issueCode"]?.GetValue<string>());
        Assert.Contains(problem["errors"]!.AsObject(), error => error.Key.EndsWith(field, StringComparison.Ordinal));
    }

    /// <summary><c>POST goals</c> with a combined (multi-spec) request.</summary>
    public static Task<HttpResponseMessage> PostGoalAsync(this HttpClient client, CreateCombinedGoalsRequest request) =>
        client.PostAsJsonAsync("/api/v1/goals", request, Ct);

    /// <summary>The single goal a one-spec creation returned.</summary>
    public static async Task<GoalDetailResponse> ReadCreatedGoalAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(Ct))!.Goals.Single();
    }

    public static async Task<GoalDetailResponse> CreateGoalAsync(this HttpClient client, CreateGoalRequest request) =>
        await (await client.PostGoalAsync(request)).ReadCreatedGoalAsync();

    public static async Task<ListGoalsResponse> ListGoalsAsync(this HttpClient client, bool archived = false) =>
        (await client.GetFromJsonAsync<ListGoalsResponse>($"/api/v1/goals{(archived ? "?archived=true" : "")}", Ct))!;

    /// <summary>Transitions goals with <c>POST goals/status</c>, reading the current order revision first unless
    /// one is given.</summary>
    public static async Task<HttpResponseMessage> PostStatusAsync(
        this HttpClient client, GoalStatus status, long? expectedOrderRevision, params Guid[] goalIds)
    {
        expectedOrderRevision ??= (await client.ListGoalsAsync()).OrderRevision;
        return await client.PostAsJsonAsync(
            "/api/v1/goals/status", new UpdateGoalsStatusRequest([.. goalIds], status, expectedOrderRevision), Ct);
    }

    public static Task<HttpResponseMessage> PostStatusAsync(this HttpClient client, Guid goalId, GoalStatus status) =>
        client.PostStatusAsync(status, null, goalId);

    /// <summary>The one goal a status change answered with.</summary>
    public static async Task<GoalDetailResponse> ReadStatusGoalAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UpdateGoalsStatusResponse>(Ct))!.Goals.Single();
    }

    /// <summary><c>PUT goals/{goalId}</c> with any combination of sections.</summary>
    public static Task<HttpResponseMessage> EditGoalAsync(this HttpClient client, Guid goalId, EditGoalRequest request) =>
        client.PutAsJsonAsync($"/api/v1/goals/{goalId}", request, Ct);

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
            $"/api/v1/projects{(archived ? "?archived=true" : "")}", Ct);
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

    /// <summary>Seeds <paramref name="count"/> Rank goals of <paramref name="status"/> (Completed or Archived, so
    /// outside the in-flight order) straight into the profile of <paramref name="subject"/>: the per-type cap
    /// without hundreds of requests.</summary>
    public static async Task<List<Guid>> SeedRankGoalsAsync(
        PlannerApiFactory factory, string subject, int count, GoalStatus status, string entityId = "blackTerminator")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = (await db.Profiles.IgnoreQueryFilters().FirstAsync(entity => entity.Subject == subject, Ct)).Id;
        var goals = Enumerable.Range(0, count).Select(_ => new Goal(GoalEntityType.Character, entityId, GoalType.Rank)
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Status = status,
            Config = new GoalConfig { Rank = new RankTarget { Start = 0, End = 15 } },
        }).ToList();
        db.Goals.AddRange(goals);
        await db.SaveChangesAsync(Ct);
        return goals.Select(goal => goal.Id.Value).ToList();
    }

    /// <summary>Seeds custom projects until the profile of <paramref name="subject"/> holds <paramref name="total"/>.</summary>
    public static async Task SeedProjectsUpToAsync(PlannerApiFactory factory, string subject, int total)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profileId = (await db.Profiles.IgnoreQueryFilters().FirstAsync(entity => entity.Subject == subject, Ct)).Id;
        var existing = await db.Projects.IgnoreQueryFilters().CountAsync(project => project.ProfileId == profileId, Ct);
        db.Projects.AddRange(Enumerable.Range(existing, total - existing).Select(i => new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = $"Seeded {i}",
            Status = ProjectStatus.Active,
            Type = ProjectType.Custom,
        }));
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The problem's <c>current</c> resource, read as <typeparamref name="T"/>.</summary>
    public static async Task<T> ReadCurrentAsync<T>(this HttpResponseMessage response) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(
            (await response.ReadProblemAsync())["current"], TestJson.Options)!;
}

/// <summary>The single-goal creation shape, test-side: <c>POST goals</c> takes the combined request, so this
/// becomes a one-spec request (<see cref="ToCombined"/>).</summary>
public sealed record CreateGoalRequest(
    GoalEntityType EntityType,
    string EntityId,
    GoalType GoalType,
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
