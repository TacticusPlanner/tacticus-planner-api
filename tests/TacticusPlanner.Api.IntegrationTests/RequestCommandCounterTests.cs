using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class RequestCommandCounterTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task GoalsRequestsHaveIndependentCountersExcludingSetupAndAssertionSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var baseline = await client.GetAsync("/api/v1/me/goals", ct);
        baseline.EnsureSuccessStatusCode();
        var expected = factory.CommandsFor(baseline);
        Assert.True(expected > 0);
        var responses = await Task.WhenAll(
            client.GetAsync("/api/v1/me/goals", ct),
            client.GetAsync("/api/v1/me/goals", ct));
        Assert.NotEqual(responses[0].Headers.GetValues(RequestCommandCounter.Header).Single(),
            responses[1].Headers.GetValues(RequestCommandCounter.Header).Single());
        Assert.All(responses, response =>
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal(expected, factory.CommandsFor(response));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        _ = await db.Profiles.IgnoreQueryFilters().CountAsync(ct);
        Assert.Equal(expected, factory.CommandsFor(baseline));
        Assert.All(responses, response => Assert.Equal(expected, factory.CommandsFor(response)));
    }

    [Fact]
    public async Task EachSyncAndAsyncCommandExecutionIsCountedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var command = db.GetService<IRawSqlCommandBuilder>().Build("SELECT 1");
        var parameters = new RelationalCommandParameterObject(
            db.GetService<IRelationalConnection>(), null, null, db,
            db.GetService<IRelationalCommandDiagnosticsLogger>());
        using var response = new HttpResponseMessage();
        var identifier = Guid.NewGuid().ToString();
        response.Headers.Add(RequestCommandCounter.Header, identifier);
        // Migration and setup have already run without any request context.
        Assert.Equal(0, factory.CommandsFor(response));
        accessor.HttpContext = new DefaultHttpContext { TraceIdentifier = identifier };
        try
        {
            _ = command.ExecuteScalar(parameters);
            _ = await command.ExecuteScalarAsync(parameters, ct);
            using (var reader = command.ExecuteReader(parameters))
                Assert.True(reader.Read());
            await using (var reader = await command.ExecuteReaderAsync(parameters, ct))
                Assert.True(await reader.ReadAsync(ct));
            _ = command.ExecuteNonQuery(parameters);
            _ = await command.ExecuteNonQueryAsync(parameters, ct);
            Assert.Equal(6, factory.CommandsFor(response));
        }
        finally
        {
            accessor.HttpContext = null;
        }
        _ = await command.ExecuteScalarAsync(parameters, ct);
        Assert.Equal(6, factory.CommandsFor(response));
    }
}
