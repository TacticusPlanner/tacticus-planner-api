using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Api.Features.V1Import;

namespace TacticusPlanner.Api.Tests;

public sealed class DisplayNameEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("ada@example.com")]
    public async Task ProviderNameIsOnlyAPrivateSuggestion(string providerName)
    {
        var client = CreateClient(name: providerName);

        var me = await GetMeAsync(client);

        Assert.Null(me.DisplayName);
        Assert.Equal(providerName, me.SuggestedDisplayName);
    }

    [Fact]
    public async Task AbsentProviderNameLeavesNoSuggestion()
    {
        var client = CreateClient(omitName: true);

        var me = await GetMeAsync(client);

        Assert.Null(me.DisplayName);
        Assert.Null(me.SuggestedDisplayName);
    }

    [Fact]
    public async Task ConfirmingTrimsSavesAndSurvivesReload()
    {
        var client = CreateClient(name: "ada@example.com");
        await GetMeAsync(client);

        var response = await client.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = "  Ada  " }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<UpdateDisplayNameResponse>(Ct);
        Assert.Equal(new UpdateDisplayNameResponse("Ada"), saved);

        var me = await GetMeAsync(client);
        Assert.Equal("Ada", me.DisplayName);
        Assert.Null(me.SuggestedDisplayName);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("bad\u0007name")]
    [InlineData("line\nbreak")]
    public async Task InvalidNamesAreRejectedWithoutChangingTheProfile(string invalid)
    {
        var client = CreateClient(name: "Suggested");
        await GetMeAsync(client);
        await client.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = "Kept" }, Ct);

        var response = await client.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = invalid }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var me = await GetMeAsync(client);
        Assert.Equal("Kept", me.DisplayName);
    }

    [Fact]
    public async Task NameLongerThanEightyCharactersIsRejectedButEightyIsAccepted()
    {
        var client = CreateClient();
        await GetMeAsync(client);

        var tooLong = await client.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = new string('a', 81) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Null((await GetMeAsync(client)).DisplayName);

        var exact = await client.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = new string('a', 80) }, Ct);
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [Fact]
    public async Task UpdateOnlyChangesTheCallersProfile()
    {
        var first = CreateClient(name: "First Suggestion");
        var second = CreateClient(name: "Second Suggestion");
        await GetMeAsync(first);
        await GetMeAsync(second);

        await first.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = "First Chosen" }, Ct);

        var other = await GetMeAsync(second);
        Assert.Null(other.DisplayName);
        Assert.Equal("Second Suggestion", other.SuggestedDisplayName);
    }

    [Fact]
    public async Task UpdateRequiresAuthentication()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/display-name")
        {
            Content = JsonContent.Create(new { displayName = "Nope" }),
        };
        request.Headers.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task V1LoginReturnsAPrivateSuggestionWithoutStoringIt()
    {
        var client = CreateClient(name: "Provider Name");
        await GetMeAsync(client);

        var body = await ImportAsync(client, FakeTacticusV1Client.ValidUsername);

        Assert.Equal(FakeTacticusV1Client.ValidUsername, body.SuggestedDisplayName);
        var me = await GetMeAsync(client);
        Assert.Equal("Provider Name", me.SuggestedDisplayName);
        Assert.Null(me.DisplayName);
    }

    [Fact]
    public async Task FailedV1LoginReturnsNoSuggestion()
    {
        var client = CreateClient(name: "Provider Name");
        await GetMeAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import", V1Request(FakeTacticusV1Client.ValidUsername, "wrong-password"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await GetMeAsync(client)).DisplayName);
    }

    [Fact]
    public async Task LaterV1ImportPreservesTheChosenName()
    {
        var client = CreateClient(name: "Provider Name");
        await GetMeAsync(client);
        await client.PutAsJsonAsync("/api/v1/me/display-name", new { displayName = "My Planner Name" }, Ct);

        var body = await ImportAsync(client, FakeTacticusV1Client.ValidUsername);

        Assert.Null(body.SuggestedDisplayName);
        var me = await GetMeAsync(client);
        Assert.Equal("My Planner Name", me.DisplayName);
    }

    private static async Task<ImportV1ProfileResponse> ImportAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/v1/me/v1-import", V1Request(username), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(Ct))!;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ImportV1ProfileRequest V1Request(string username, string password = FakeTacticusV1Client.ValidPassword) =>
        new(username, password, new ImportV1Selection(true, true, false, false, false, false));

    private static async Task<CurrentUserResponse> GetMeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/me", Ct))!;

    private HttpClient CreateClient(string? name = null, bool omitName = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"display-name-{Guid.NewGuid()}");
        if (name is not null)
        {
            client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NameHeader, name);
        }

        if (omitName)
        {
            client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.OmitNameHeader, "1");
        }

        return client;
    }
}
