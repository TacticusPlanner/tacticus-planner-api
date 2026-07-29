namespace TacticusPlanner.Api.Tests;

/// <summary>Shared setup for Goals/Projects endpoint tests: provisions an authenticated client (the
/// Account + Profile these endpoints require) without any Tacticus-specific integration.</summary>
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
}
