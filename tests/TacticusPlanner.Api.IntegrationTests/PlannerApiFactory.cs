using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TacticusPlanner.Api.Features.Analytics;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>HTTP host with production persistence and a uniquely owned, migrated PostgreSQL database.</summary>
public sealed class PlannerApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgresAssemblyFixture.DatabaseLease database = PostgresAssemblyFixture.Shared.CreateDatabase();
    private int databaseDisposed;

    internal FakeTacticusApi TacticusApi { get; } = new();
    internal FakeTacticusV1Client V1Client { get; } = new();
    internal RecordingProductAnalytics Analytics { get; } = new();

    internal Func<PlannerDbContext, bool>? FailAfterSave { get; set; }
    internal bool FailureInjected { get; set; }

    public int CommandsFor(HttpResponseMessage response) => Services.GetRequiredService<RequestCommandCounter>().CommandsFor(response);

    public string ConnectionString => database.ConnectionString;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Interlocked.Exchange(ref databaseDisposed, 1) == 0)
        {
            database.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Interlocked.Exchange(ref databaseDisposed, 1) == 0)
        {
            database.Dispose();
        }
    }

    /// <summary>The fixed <c>Analytics:IdentityKey</c> this factory supplies. No <c>Analytics:ProjectToken</c>
    /// is supplied, so the suite exercises real id derivation while the capture path stays inert by
    /// construction (design.md's "identity key is required; the project token is optional" decision).
    /// Exposed so tests can independently derive the expected analytics id for a caller.</summary>
    public static readonly byte[] TestAnalyticsIdentityKey = [.. Enumerable.Repeat((byte)0x42, 32)];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Analytics:ProjectToken", "");
        builder.UseSetting("ConnectionStrings:planner-db", ConnectionString);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:planner-db"] = ConnectionString,
                ["Authentication:Authority"] = "https://example.ciamlogin.com/example.onmicrosoft.com/v2.0",
                ["Authentication:Audience"] = "api://tacticus-planner-api-test",
                ["TacticusApi:BaseUrl"] = "https://api.tacticusgame.com",
                ["V1Api:BaseUrl"] = "https://tacticus.example.com",
                ["ColumnEncryption:CurrentKeyVersion"] = "v1",
                ["ColumnEncryption:Keys:v1"] = Convert.ToBase64String(new byte[32]),
                ["UserJot:ProjectId"] = "test-userjot-project",
                ["UserJot:ProjectSecret"] = "test-userjot-project-secret-for-tests",
                ["Analytics:IdentityKey"] = Convert.ToBase64String(TestAnalyticsIdentityKey),
                // Explicitly cleared: appsettings.Development.json now ships a real PostHog project
                // token for local `dotnet run`, and this in-memory source is the last one added, so
                // without this override the test host would pick that real token up and register the
                // live PostHog SDK client instead of the inert path - see AGENTS.md's "no outbound
                // network calls" rule for this test project.
                ["Analytics:ProjectToken"] = "",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = PlannerTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = PlannerTestAuthenticationHandler.SchemeName;
                    options.DefaultScheme = PlannerTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, PlannerTestAuthenticationHandler>(
                    PlannerTestAuthenticationHandler.SchemeName,
                    _ => { }
                );

            services.AddHttpContextAccessor();
            services.AddSingleton<RequestCommandCounter>();
            services.AddSingleton<IStartupFilter, RequestCorrelationFilter>();
            services.ConfigureDbContext<PlannerDbContext>((sp, options) => options.AddInterceptors(
                sp.GetRequiredService<RequestCommandCounter>(), new PersistenceFailureInterceptor(this)));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, PostgresPrecisionTimeProvider>();

            services.RemoveAll<ITacticusApi>();
            services.AddSingleton<ITacticusApi>(TacticusApi);

            services.RemoveAll<ITacticusV1Client>();
            services.AddSingleton<ITacticusV1Client>(V1Client);

            services.RemoveAll<IProductAnalytics>();
            services.AddSingleton<IProductAnalytics>(Analytics);
        });
    }
}

/// <summary>Keep exact round-trip assertions while supplying timestamps PostgreSQL can represent.</summary>
internal sealed class PostgresPrecisionTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        var now = TimeProvider.System.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }
}
