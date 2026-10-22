using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>The routes the foundation rebuild removed are not served (and answer in the shared problem shape).</summary>
public sealed class RemovedRoutesTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Theory]
    [InlineData("POST", "goals/combined")]
    [InlineData("PUT", "goals/{goal}/target")]
    [InlineData("PUT", "goals/{goal}/projects")]
    [InlineData("PUT", "goals/{goal}/edit")]
    [InlineData("POST", "goals/{goal}/status")]
    [InlineData("POST", "projects/{project}/goals/status")]
    [InlineData("GET", "projects/{project}/goals")]
    [InlineData("POST", "tacticus-integration/player-sync")]
    [InlineData("POST", "tacticus-api-key/validate")]
    public async Task RemovedRouteIsNotFoundOrNotAllowed(string method, string route)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var goal = await client.CreateGoalAsync(new CreateGoalRequest(
            GoalEntityType.Character, "blackTerminator", GoalType.Unlock, new(), null));
        var path = "/api/v1/" + route
            .Replace("{goal}", goal.GoalId.ToString(), StringComparison.Ordinal)
            .Replace("{project}", goal.ProjectIds.Single().ToString(), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"{method} {route} answered {(int)response.StatusCode}");
        var issueCode = await response.ReadIssueCodeAsync();
        Assert.True(issueCode is "notFound" or "methodNotAllowed", issueCode);
        if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
            Assert.NotEmpty(response.Content.Headers.Allow);
    }
}
