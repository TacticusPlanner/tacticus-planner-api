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
/// Covers the `add-goals-cards-view` design.md risk that EF might materialize a missing (or unsupported)
/// <c>GoalsView</c> JSON property differently than assumed: only a real Postgres `jsonb` column round-trips
/// the stored text, so this proves rows saved before the field existed read back as the <c>Cards</c> default.
/// </summary>
public sealed class UserSettingsGoalsViewPostgresTests
{
    [Theory]
    [InlineData("""{"DailyEnergy": 638, "XpBookRarity": "Epic"}""")]
    [InlineData("""{"DailyEnergy": 638, "XpBookRarity": "Epic", "GoalsView": "Grid"}""")]
    public async Task LegacyOrUnsupportedGoalsViewReadsAsTheCardsDefault(string settingsJson)
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
                INSERT INTO profiles (id, issuer, subject, display_name, created_at, updated_at)
                VALUES (@profile, 'test', 'legacy-goals-view', 'Legacy Settings Profile', now(), now());
                INSERT INTO user_settings (id, revision, created_at, updated_at, settings)
                VALUES (@profile, 0, now(), now(), @settings::jsonb);
                """;
            command.Parameters.AddWithValue("profile", profileId);
            command.Parameters.AddWithValue("settings", settingsJson);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var db = new PlannerDbContext(
            options, new PassthroughEncryption(), new StaticProfile(ProfileId.From(profileId)));
        var settings = await db.UserSettings.FirstAsync(
            entity => entity.Id == ProfileId.From(profileId), TestContext.Current.CancellationToken);
        var response = UserSettingsResponse.From(settings);

        Assert.Equal(638, response.DailyEnergy);
        Assert.Equal(XpBookRarity.Epic, response.XpBookRarity);
        Assert.Equal(GoalsView.Cards, response.GoalsView);
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
