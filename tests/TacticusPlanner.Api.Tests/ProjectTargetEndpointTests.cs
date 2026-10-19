using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TacticusPlanner.Api.Features.Projects;

namespace TacticusPlanner.Api.Tests;

/// <summary>Spec: project-event-target — a project's optional Legendary Event target.</summary>
public sealed class ProjectTargetEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string ProjectsUrl = "/api/v1/me/projects";
    private static readonly ProjectTargetDto Uthar = new("legendaryEvent", "votanUthar");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateWithTargetReturnsIt()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var project = await CreateAsync(client, Uthar);

        Assert.Equal(Uthar, project.Target);
        var list = await client.GetFromJsonAsync<ListProjectsResponse>(ProjectsUrl, Ct);
        Assert.Equal(Uthar, list!.Projects.Single(entry => entry.ProjectId == project.ProjectId).Target);
    }

    [Fact]
    public async Task ProjectWithoutTargetServesNull()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var project = await CreateAsync(client, null);

        Assert.Null(project.Target);
    }

    [Theory]
    [InlineData("legendaryEvent", "notAnEvent", "target.eventId")]
    [InlineData("guildRaid", "votanUthar", "target.kind")]
    public async Task CreateRejectsAnInvalidTarget(string kind, string eventId, string field)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            ProjectsUrl, new CreateProjectRequest("Uthar run 3", null, null, new ProjectTargetDto(kind, eventId)), Ct);

        await AssertBadRequestAsync(response, field);
    }

    [Fact]
    public async Task DefaultProjectCannotHaveATarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var list = await client.GetFromJsonAsync<ListProjectsResponse>(ProjectsUrl, Ct);
        var defaultProject = list!.Projects.Single(project => project.IsDefault);

        var response = await client.PutAsJsonAsync(
            $"{ProjectsUrl}/{defaultProject.ProjectId}",
            new UpdateProjectRequest(defaultProject.Name, null, null, "Active", defaultProject.Revision, Uthar),
            Ct);

        await AssertBadRequestAsync(response, "target");
    }

    [Fact]
    public async Task UpdateWithoutTargetClearsIt()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await CreateAsync(client, Uthar);

        var updated = await UpdateAsync(client, project, "Active", null);

        Assert.Null(updated.Target);
    }

    [Fact]
    public async Task ArchivingKeepsTheTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await CreateAsync(client, Uthar);

        var archived = await UpdateAsync(client, project, "Archived", Uthar);
        var restored = await UpdateAsync(client, archived, "Active", Uthar);

        Assert.Equal(Uthar, archived.Target);
        Assert.Equal(Uthar, restored.Target);
    }

    private static async Task<ProjectSummaryResponse> CreateAsync(HttpClient client, ProjectTargetDto? target)
    {
        var response = await client.PostAsJsonAsync(
            ProjectsUrl, new CreateProjectRequest("Uthar run 3", null, null, target), Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(Ct))!;
    }

    private static async Task<ProjectSummaryResponse> UpdateAsync(
        HttpClient client, ProjectSummaryResponse project, string status, ProjectTargetDto? target)
    {
        var response = await client.PutAsJsonAsync(
            $"{ProjectsUrl}/{project.ProjectId}",
            new UpdateProjectRequest(project.Name, null, null, status, project.Revision, target),
            Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(Ct))!;
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string field)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var document = JsonDocument.Parse(body);
        Assert.Contains(field,
            document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            StringComparer.OrdinalIgnoreCase);
    }
}
