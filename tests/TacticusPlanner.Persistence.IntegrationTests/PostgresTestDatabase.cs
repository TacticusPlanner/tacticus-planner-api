using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>A disposable PostgreSQL migrated to the current model, with one seeded profile and its projects.
/// Contexts are built like production's (snake case, metadata interceptor, retrying execution strategy) with
/// passthrough column encryption.</summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer container;

    private PostgresTestDatabase(PostgreSqlContainer container) => this.container = container;

    public string ConnectionString => container.GetConnectionString();

    public ProfileId ProfileId { get; } = ProfileId.From(Guid.CreateVersion7());

    public List<ProjectId> ProjectIds { get; } = [];

    public static async Task<PostgresTestDatabase> StartAsync(int projects = 1)
    {
        var container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        var database = new PostgresTestDatabase(container);
        await using (var db = database.CreateContext(profileId: null))
        {
            await db.Database.GetService<IMigrator>().MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        await database.SeedProfileAsync(database.ProfileId, projects);
        return database;
    }

    public DbContextOptions<PlannerDbContext> Options =>
        new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(ConnectionString, options => options.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            // Passthrough encryption/profile services change captured converter instances, not the schema.
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    /// <summary>A context scoped to <paramref name="profileId"/> (the seeded profile by default; null for
    /// none).</summary>
    public PlannerDbContext CreateContext(ProfileId? profileId) =>
        new(Options, new PassthroughEncryption(), new StaticProfile(profileId));

    public PlannerDbContext CreateContext() => CreateContext(ProfileId);

    /// <summary>Adds a profile with <paramref name="projects"/> custom projects (their ids land in
    /// <see cref="ProjectIds"/> for the seeded profile).</summary>
    public async Task SeedProfileAsync(ProfileId profileId, int projects = 1)
    {
        await using var db = CreateContext(profileId);
        db.Profiles.Add(new Profile
        {
            Id = profileId,
            Issuer = "test",
            Subject = profileId.Value.ToString(),
            DisplayName = "Test",
        });
        for (var index = 0; index < projects; index++)
        {
            var project = new Project
            {
                Id = ProjectId.From(Guid.CreateVersion7()),
                ProfileId = profileId,
                Name = $"Project {index + 1}",
                Status = ProjectStatus.Active,
                Type = ProjectType.Custom,
            };
            db.Projects.Add(project);
            if (profileId == ProfileId)
                ProjectIds.Add(project.Id);
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext) => plaintext;

        public string? Decrypt(string? envelope) => envelope;
    }

    private sealed class StaticProfile(ProfileId? profileId) : ICurrentProfileProvider
    {
        public ProfileId? ProfileId { get; } = profileId;
    }
}
