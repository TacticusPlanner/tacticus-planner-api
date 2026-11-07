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
        var response = await ReadStoredAsync("""{"DailyEnergy": 638}""");

        Assert.Equal(638, response.DailyEnergy);
        Assert.Equal(XpBookRarity.Legendary, response.XpBookRarity);
        Assert.Empty(response.Preferences);
    }

    [Fact]
    public async Task LegacyWatchAdsOffReadsAsTheSkipDailyAdsPreference()
    {
        var response = await ReadStoredAsync("""{"DailyEnergy": 288, "WatchAds": false, "Preferences": 1}""");

        Assert.Equal(
            [UserPreference.LegendaryEventShowPaidOptions, UserPreference.LegendaryEventSkipDailyAds],
            response.Preferences);
    }

    /// <summary>The response read for a user_settings row whose settings JSON is <paramref name="settingsJson"/>.</summary>
    private static async Task<UserSettingsResponse> ReadStoredAsync(string settingsJson)
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
                VALUES (@profile, 'test', 'legacy-settings', 'Legacy Settings Profile', now(), now());
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
        return UserSettingsResponse.From(settings);
    }

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext, string column) => plaintext;

        public string? Decrypt(string? envelope, string column) => envelope;
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
