using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class IntegrationHostTests
{
    [Fact]
    public async Task UnavailableDockerFailsSetupWithActionableInstructions()
    {
        var fixture = new PostgresAssemblyFixture(() => new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:18-alpine")
            .WithDockerEndpoint("tcp://127.0.0.1:1").Build());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.InitializeAsync());
        Assert.Contains("Start Docker Desktop", exception.Message);
        Assert.Contains("docker info", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task ExternalBoundariesAndAuthenticationRemainFactoryOwned()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var first = new PlannerApiFactory();
        await using var second = new PlannerApiFactory();
        const string token = "same-token-in-two-factories";
        first.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());
        Assert.NotNull((await first.TacticusApi.GetPlayerAsync(token, ct)).Player.Details);
        Assert.Null((await second.TacticusApi.GetPlayerAsync(token, ct)).Player.Details);
        first.Analytics.AccountRegistered("same-id");
        Assert.Single(first.Analytics.GetEvents("same-id"));
        Assert.Empty(second.Analytics.GetEvents("same-id"));
        var username = first.V1Client.ConfigureProfile(new TacticusPlanner.Api.Features.V1Import.TacticusV1Profile(null, null));
        var accessToken = await first.V1Client.LoginAsync(username, FakeTacticusV1Client.ValidPassword, ct);
        Assert.NotNull(accessToken);
        Assert.Null(await second.V1Client.LoginAsync(username, FakeTacticusV1Client.ValidPassword, ct));
        var client = first.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account", ct)).StatusCode);
        client.DefaultRequestHeaders.Remove(PlannerTestAuthenticationHandler.NoAuthHeader);
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, "custom-subject");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/account", ct)).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedHostsMigratePersistAndOwnOnlyTheirDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new PlannerApiFactory();
        await using var second = new PlannerApiFactory();
        var firstConnectionString = first.ConnectionString;
        try
        {
            var responses = await Task.WhenAll(
                first.CreateClient().GetAsync("/api/v1/account", ct),
                second.CreateClient().GetAsync("/api/v1/account", ct));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            await using var scopeA = first.Services.CreateAsyncScope();
            await using var scopeB = second.Services.CreateAsyncScope();
            var dbA = scopeA.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var dbB = scopeB.ServiceProvider.GetRequiredService<PlannerDbContext>();
            Assert.True(dbA.Database.CreateExecutionStrategy().RetriesOnFailure);
            Assert.Empty(await dbA.Database.GetPendingMigrationsAsync(ct));
            var profileA = await dbA.Profiles.IgnoreQueryFilters().SingleAsync(ct);
            var profileB = await dbB.Profiles.IgnoreQueryFilters().SingleAsync(ct);
            Assert.NotEqual(profileA.Id, profileB.Id);
            Assert.False(await dbB.Profiles.IgnoreQueryFilters().AnyAsync(profile => profile.Id == profileA.Id, ct));
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using var connection = new NpgsqlConnection(second.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pg_database WHERE datname = @name";
        command.Parameters.AddWithValue("name", new NpgsqlConnectionStringBuilder(firstConnectionString).Database!);
        Assert.Equal(0L, await command.ExecuteScalarAsync(ct));
        Assert.Equal(HttpStatusCode.OK, (await second.CreateClient().GetAsync("/api/v1/account", ct)).StatusCode);
    }
}
