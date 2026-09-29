using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Api.Features.UserSettings;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Covers the `surface-goal-farming-guidance` design.md risk that EF might materialize a missing JSON
/// property differently than assumed: the InMemory-backed <c>TacticusPlanner.Api.Tests</c> suite never
/// round-trips real JSON text (its `ToJson` owned entities stay as in-memory object graphs), so only a
/// real Postgres `jsonb` column can prove that a row saved before `xpBookRarity` existed reads back as
/// the `Legendary` default rather than throwing or materializing something else.
/// </summary>
public sealed class UserSettingsXpBookRarityPostgresTests
{
    [Fact]
    public async Task LegacySettingsJsonWithoutTheRarityReadsAsTheLegendaryDefault()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var migrationDb = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var migrator = migrationDb.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        var profileId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (gen_random_uuid(), 'test', 'legacy-xp-book-rarity', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                SELECT @profile, id, 'Legacy Settings Profile', now(), now() FROM accounts WHERE subject = 'legacy-xp-book-rarity';
                INSERT INTO user_settings (id, revision, created_at, updated_at, settings)
                VALUES (@profile, 0, now(), now(), '{"DailyEnergy": 638}'::jsonb);
                """;
            command.Parameters.AddWithValue("profile", profileId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var db = new PlannerDbContext(
            options, new PassthroughEncryption(), new StaticProfile(ProfileId.From(profileId)));
        var settings = await db.UserSettings.FirstAsync(
            entity => entity.Id == ProfileId.From(profileId), TestContext.Current.CancellationToken);
        var response = UserSettingsResponse.From(settings);

        Assert.Equal(638, response.DailyEnergy);
        Assert.Equal("Legendary", response.XpBookRarity);
    }

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext) => plaintext;

        public string? Decrypt(string? envelope) => envelope;
    }

    private sealed class NoProfile : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => null;
    }

    private sealed class StaticProfile(ProfileId profileId) : ICurrentProfileProvider
    {
        public ProfileId? ProfileId { get; } = profileId;
    }
}
