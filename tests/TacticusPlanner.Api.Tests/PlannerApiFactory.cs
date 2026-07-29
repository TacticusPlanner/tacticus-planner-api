using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Interceptors;
using TacticusPlanner.TacticusApi;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Boots the API in-memory with a stub auth handler, an EF Core InMemory-backed <see cref="PlannerDbContext"/>
/// (a fresh database per factory instance), and fake Tacticus/V1 clients — so account, Tacticus-integration, and
/// V1-import endpoints can be exercised without a live PostgreSQL instance or outbound network calls.
/// </summary>
public sealed class PlannerApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"planner-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Unused once the DbContext registration below is swapped to the InMemory provider, but Aspire's
                // AddNpgsqlDbContext validates this key is present while the host builds.
                ["ConnectionStrings:planner-db"] = "Host=localhost;Port=5432;Database=unused;Username=postgres;Password=postgres",
                ["Authentication:Authority"] = "https://example.ciamlogin.com/example.onmicrosoft.com/v2.0",
                ["Authentication:Audience"] = "api://tacticus-planner-api-test",
                ["TacticusApi:BaseUrl"] = "https://api.tacticusgame.com",
                ["V1Api:BaseUrl"] = "https://tacticus.example.com",
                ["ColumnEncryption:CurrentKeyVersion"] = "v1",
                ["ColumnEncryption:Keys:v1"] = Convert.ToBase64String(new byte[32]),
            });
        });
        builder.ConfigureTestServices(services =>
        {
            var migrationService = services.Single(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(DatabaseMigrationHostedService)
            );
            services.Remove(migrationService);

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

            // Aspire's AddNpgsqlDbContext registers a pooled context (a singleton IDbContextPool<PlannerDbContext>
            // plus related scoped lease/options services). Strip every service closed over PlannerDbContext before
            // re-registering a plain (non-pooled) InMemory context, otherwise the singleton pool is left depending
            // on the scoped DbContextOptions this block adds below and DI validation fails at host startup.
            var plannerDbContextDescriptors = services
                .Where(descriptor => descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericArguments().Contains(typeof(PlannerDbContext)))
                .ToList();

            foreach (var descriptor in plannerDbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<PlannerDbContext>();
            services.AddDbContext<PlannerDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(databaseName);
                options.ReplaceService<IExecutionStrategyFactory, TrackingExecutionStrategyFactory>();
                options.AddInterceptors(
                    sp.GetRequiredService<EntityMetadataInterceptor>(),
                    new ImportedGoalExecutionStrategyGuard()
                );
            });

            services.RemoveAll<ITacticusApi>();
            services.AddScoped<ITacticusApi, FakeTacticusApi>();

            services.RemoveAll<ITacticusV1Client>();
            services.AddScoped<ITacticusV1Client, FakeTacticusV1Client>();
        });
    }
}

/// <summary>
/// Makes execution-strategy scope observable to the import regression guard while retaining the
/// in-memory provider's no-retry behavior.
/// </summary>
internal sealed class TrackingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
    : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new TrackingExecutionStrategy(dependencies.CurrentContext.Context);
}

internal sealed class TrackingExecutionStrategy(DbContext context) : IExecutionStrategy
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool IsExecuting => Depth.Value > 0;

    public bool RetriesOnFailure => false;

    public TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        Depth.Value++;
        try
        {
            return operation(context, state);
        }
        finally
        {
            Depth.Value--;
        }
    }

    public async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        Depth.Value++;
        try
        {
            return await operation(context, state, cancellationToken);
        }
        finally
        {
            Depth.Value--;
        }
    }
}

/// <summary>
/// Fails the V1 fixture's imported-goal save unless it is inside the configured execution strategy.
/// This reproduces the production constraint that the in-memory provider does not enforce itself.
/// </summary>
internal sealed class ImportedGoalExecutionStrategyGuard : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var importsGoals = eventData.Context?.ChangeTracker.Entries<Goal>()
            .Any(entry => entry.State == EntityState.Added && entry.Entity.Notes == "Imported note") == true;
        if (importsGoals && !TrackingExecutionStrategy.IsExecuting)
        {
            throw new InvalidOperationException("Imported goals must be saved inside the configured execution strategy.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// Authenticates every request with claims driven by the <see cref="IssuerHeader"/>/<see cref="SubjectHeader"/>
/// request headers (defaulting to a fixed test issuer/subject), so individual tests can simulate distinct signed-in
/// users without colliding on the same Account row. Send <see cref="NoAuthHeader"/> to exercise anonymous access.
/// </summary>
public sealed class PlannerTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PlannerTest";
    public const string NoAuthHeader = "X-Test-NoAuth";
    public const string IssuerHeader = "X-Test-Issuer";
    public const string SubjectHeader = "X-Test-Subject";
    public const string DefaultIssuer = "https://example.ciamlogin.com/example.onmicrosoft.com/v2.0";
    public const string DefaultSubject = "test-user";

    public PlannerTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    )
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(NoAuthHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim("iss", GetHeaderOrDefault(IssuerHeader, DefaultIssuer)),
            new Claim("sub", GetHeaderOrDefault(SubjectHeader, DefaultSubject)),
            new Claim("scp", "access_as_user"),
            new Claim("name", "Test User"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string GetHeaderOrDefault(string headerName, string defaultValue)
    {
        return Request.Headers.TryGetValue(headerName, out var values)
            && values.Count > 0
            && !string.IsNullOrEmpty(values[0])
            ? values[0]!
            : defaultValue;
    }
}
