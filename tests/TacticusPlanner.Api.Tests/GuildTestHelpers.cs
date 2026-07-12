using System.Net.Http.Json;
using TacticusPlanner.Api.Features.TacticusIntegration;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Shared setup for Guild Phase 1 endpoint tests: provisions an authenticated client with a configured,
/// Guid-parseable Tacticus User ID, since <see cref="Features.Guilds.GuildSyncService"/> requires the
/// caller's Tacticus User ID to parse as a <see cref="Guid"/> before it can be matched against a fresh
/// upstream roster member.
/// </summary>
internal static class GuildTestHelpers
{
    public static async Task<(HttpClient Client, Guid TacticusUserId)> CreateGuildReadyClientAsync(
        PlannerApiFactory factory,
        string? subject = null
    )
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            PlannerTestAuthenticationHandler.SubjectHeader,
            subject ?? $"guild-{Guid.NewGuid()}"
        );

        // /me auto-provisions the Account + Profile these endpoints require to exist.
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        var tacticusUserId = Guid.NewGuid();
        var integrationResponse = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, tacticusUserId.ToString()),
            TestContext.Current.CancellationToken
        );
        integrationResponse.EnsureSuccessStatusCode();

        return (client, tacticusUserId);
    }
}
