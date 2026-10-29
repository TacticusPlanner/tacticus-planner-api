using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

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

        // GET /account auto-provisions the Account + Profile these endpoints require to exist.
        await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken);

        var tacticusUserId = Guid.NewGuid();
        var integrationResponse = await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(FakeTacticusApi.ValidKey, tacticusUserId.ToString()),
            TestContext.Current.CancellationToken
        );
        integrationResponse.EnsureSuccessStatusCode();

        return (client, tacticusUserId);
    }

    /// <summary>Backdates every guild's last sync attempt past <see cref="GuildSyncService.CooldownWindow"/>, so a
    /// test can <c>POST guild/sync</c> right after registering (which counts as an attempt).</summary>
    public static async Task ExpireSyncCooldownAsync(PlannerApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var expired = DateTimeOffset.UtcNow - GuildSyncService.CooldownWindow - TimeSpan.FromSeconds(1);
        await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().Guilds
            .ExecuteUpdateAsync(setters => setters.SetProperty(guild => guild.LastSyncAttemptedAt, expired),
                TestContext.Current.CancellationToken);
    }
}
