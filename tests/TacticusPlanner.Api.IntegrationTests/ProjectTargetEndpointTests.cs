using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Spec: project-event-target — a project's optional Legendary Event target.</summary>
public sealed class ProjectTargetEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string ProjectsUrl = "/api/v1/projects";
    private static readonly ProjectTargetDto Uthar = new(ProjectTargetKind.LegendaryEvent, "votanUthar");

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

    [Fact]
    public async Task CreateRejectsAnUnknownTargetEvent()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            ProjectsUrl,
            new CreateProjectRequest("Uthar run 3", null, null, new ProjectTargetDto(ProjectTargetKind.LegendaryEvent, "notAnEvent")),
            Ct);

        await AssertBadRequestAsync(response, "target.eventId");
    }

    [Fact]
    public async Task CreateRejectsAnUnknownTargetKind()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.SendMutatedJsonAsync(
            HttpMethod.Post,
            ProjectsUrl,
            new CreateProjectRequest("Uthar run 3", null, null, Uthar),
            body => body["target"]!["kind"] = "guildRaid");

        await response.AssertFieldRejectedAsync("target.kind");
        var list = await client.GetFromJsonAsync<ListProjectsResponse>(ProjectsUrl, Ct);
        Assert.DoesNotContain(list!.Projects, project => project.Name == "Uthar run 3");
    }

    [Fact]
    public async Task DefaultProjectCannotHaveATarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var list = await client.GetFromJsonAsync<ListProjectsResponse>(ProjectsUrl, Ct);
        var defaultProject = list!.Projects.Single(project => project.IsDefault);

        var response = await client.PutAsJsonAsync(
            $"{ProjectsUrl}/{defaultProject.ProjectId}",
            new UpdateProjectRequest(defaultProject.Name, null, null, ProjectStatus.Active, defaultProject.Revision, Uthar),
            Ct);

        await AssertBadRequestAsync(response, "target");
    }

    [Fact]
    public async Task UpdateWithoutTargetClearsIt()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await CreateAsync(client, Uthar);

        var updated = await UpdateAsync(client, project, ProjectStatus.Active, null);

        Assert.Null(updated.Target);
    }

    [Fact]
    public async Task UpdateWithTheTargetFieldAbsentClearsIt()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await CreateAsync(client, Uthar);

        var response = await client.PutAsJsonAsync(
            $"{ProjectsUrl}/{project.ProjectId}",
            new { name = project.Name, status = "Active", revision = project.Revision },
            Ct);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        Assert.Null((await response.Content.ReadFromJsonAsync<ProjectSummaryResponse>(Ct))!.Target);
    }

    [Fact]
    public async Task ResendingATargetWhoseEventLeftTheCatalogStillSaves()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await CreateAsync(client, Uthar);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            // IgnoreQueryFilters: this scope has no HttpContext to scope the profile filter to.
            var stored = await db.Projects.IgnoreQueryFilters()
                .SingleAsync(entity => entity.Id == ProjectId.From(project.ProjectId), Ct);
            stored.TargetEventId = "retiredEvent";
            await db.SaveChangesAsync(Ct);
        }
        var list = await client.GetFromJsonAsync<ListProjectsResponse>(ProjectsUrl, Ct);
        var current = list!.Projects.Single(entry => entry.ProjectId == project.ProjectId);
        var retired = new ProjectTargetDto(ProjectTargetKind.LegendaryEvent, "retiredEvent");

        var archived = await UpdateAsync(client, current, ProjectStatus.Archived, retired);

        Assert.Equal(retired, archived.Target);
    }

    [Fact]
    public async Task ArchivingKeepsTheTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var project = await CreateAsync(client, Uthar);

        var archived = await UpdateAsync(client, project, ProjectStatus.Archived, Uthar);
        var restored = await UpdateAsync(client, archived, ProjectStatus.Active, Uthar);

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
        HttpClient client, ProjectSummaryResponse project, ProjectStatus status, ProjectTargetDto? target)
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
