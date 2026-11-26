using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Jobs.Smoke;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;

namespace TacticusPlanner.Jobs;

public static class JobRegistration
{
    /// <summary>Every batch job, by the name passed as the container's first argument.</summary>
    public static IServiceCollection AddBatchJobs(this IServiceCollection services)
    {
        services.AddKeyedScoped<IBatchJob, SmokeJob>(SmokeJob.Name);
        return services;
    }

    /// <summary>
    /// The planner database as the API sees it (snake case, metadata interceptor), with no current profile and no
    /// column-encryption key: a job that needs either gets it explicitly when one is added.
    /// </summary>
    public static IServiceCollection AddPlannerDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("planner-db")
            ?? throw new InvalidOperationException("ConnectionStrings:planner-db is required.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<EntityMetadataInterceptor>();
        services.AddSingleton<IColumnEncryptionService, UnavailableColumnEncryption>();
        services.AddSingleton<ICurrentProfileProvider, NoCurrentProfile>();
        services.AddDbContext<PlannerDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<EntityMetadataInterceptor>()));
        services.AddScoped<IDatabaseReadiness, EfDatabaseReadiness>();
        return services;
    }
}

/// <summary>Jobs act on no signed-in profile: profile-scoped query filters match no rows.</summary>
internal sealed class NoCurrentProfile : ICurrentProfileProvider
{
    public Domain.Profiles.ProfileId? ProfileId => null;
}

/// <summary>The jobs container holds no column-encryption key; reading or writing an encrypted column fails loudly.</summary>
internal sealed class UnavailableColumnEncryption : IColumnEncryptionService
{
    public string? Encrypt(string? plaintext, string column) => throw Unavailable(column);

    public string? Decrypt(string? envelope, string column) => throw Unavailable(column);

    private static InvalidOperationException Unavailable(string column) =>
        new($"The jobs container has no column-encryption key, so it cannot read or write {column}.");
}
