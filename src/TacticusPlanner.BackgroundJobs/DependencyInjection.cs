using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace TacticusPlanner.BackgroundJobs;

public static class DependencyInjection
{
    /// <summary>The PostgreSQL schema Hangfire creates and migrates itself. EF Core never touches it.</summary>
    public const string SchemaName = "hangfire";

    /// <summary>
    /// Hangfire with PostgreSQL storage in the planner database (<see cref="SchemaName"/> schema). Enqueuing works
    /// everywhere through <see cref="IBackgroundJobClient"/>; the server that executes jobs runs only when
    /// <paramref name="runServer"/> and <see cref="BackgroundJobsOptions.ServerEnabled"/> are both true.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        bool runServer = true
    )
    {
        var options = configuration.GetSection(BackgroundJobsOptions.SectionName).Get<BackgroundJobsOptions>()
            ?? new BackgroundJobsOptions();

        // Registered before AddHangfire, whose TryAdd then keeps it: AddHangfire's own storage registration returns
        // the process-wide JobStorage.Current, which would let every test host in one process share the storage
        // (and database) of whichever host configured it last. The storage is built on first use, so hosts that
        // never enqueue (OpenAPI generation, the Docker-free tests) never connect.
        services.AddSingleton<JobStorage>(_ =>
        {
            var storageOptions = new PostgreSqlStorageOptions
            {
                SchemaName = SchemaName,
                PrepareSchemaIfNecessary = true,
            };
            return new PostgreSqlStorage(
                new NpgsqlConnectionFactory(StorageConnectionString(configuration, options), storageOptions),
                storageOptions
            );
        });

        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings());

        if (runServer && options.ServerEnabled)
        {
            services.AddHangfireServer(server =>
            {
                server.WorkerCount = options.WorkerCount;
                server.Queues = [BackgroundJobQueues.Default];
            });
        }

        return services;
    }

    private static string StorageConnectionString(IConfiguration configuration, BackgroundJobsOptions options)
    {
        var connectionString = configuration.GetConnectionString("planner-db")
            ?? throw new InvalidOperationException("ConnectionStrings:planner-db is required for background jobs.");

        // A different connection string is a different Npgsql pool, so Hangfire never takes API connections.
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = options.StoragePoolSize,
            ApplicationName = "tacticus-planner-hangfire",
        }.ConnectionString;
    }
}

public static class BackgroundJobQueues
{
    public const string Default = "default";
}
