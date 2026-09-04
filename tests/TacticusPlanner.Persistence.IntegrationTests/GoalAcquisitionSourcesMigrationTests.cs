using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>
/// Exercises the <c>AddGoalAcquisitionSources</c> data migration (openspec change
/// add-goal-acquisition-sources-config) against every scenario in
/// specs/goal-target-model/spec.md's "Existing goals migrate to an equivalent acquisition-source set"
/// requirement. Migrates to the prior migration first so the seeded rows go through the same schema the
/// migration itself expects, then applies <c>AddGoalAcquisitionSources</c> and inspects the raw jsonb.
/// </summary>
public sealed class GoalAcquisitionSourcesMigrationTests
{
    [Fact]
    public async Task MigrationRewritesEveryAcquisitionSourceScenario()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new PlannerDbContext(options, new PassthroughEncryption(), new NoProfile());
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(
            "20260809171113_RedesignGoalsProjectsManagement", TestContext.Current.CancellationToken);

        var profileId = Guid.NewGuid();
        var bothGoalId = Guid.NewGuid();
        var campaignOnlyGoalId = Guid.NewGuid();
        var unlockWithLocationsGoalId = Guid.NewGuid();
        var neitherGoalId = Guid.NewGuid();
        var rankGoalId = Guid.NewGuid();
        var nullShardIdsGoalId = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, issuer, subject, created_at, updated_at)
                VALUES (gen_random_uuid(), 'test', 'migration', now(), now());
                INSERT INTO profiles (id, account_id, display_name, created_at, updated_at)
                SELECT @profile, id, 'Migration test', now(), now() FROM accounts LIMIT 1;

                INSERT INTO goals (id, revision, profile_id, entity_type, entity_id, goal_type, status,
                                   depends_on, created_at, updated_at, config, events)
                VALUES
                (@both, 0, @profile, 'Character', 'both-unit', 'Ascension', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"AscensionFarming":{"Source":2,"ShardBattleIds":["b1","b2"],"MythicShardBattleIds":["m1"]},"FarmingLocationIds":null}',
                 '[]'),
                (@campaignOnly, 0, @profile, 'Character', 'campaign-unit', 'Ascension', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"AscensionFarming":{"Source":0,"ShardBattleIds":["r1"],"MythicShardBattleIds":[]},"FarmingLocationIds":null}',
                 '[]'),
                (@unlockLocations, 0, @profile, 'Character', 'unlock-unit', 'Unlock', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"AscensionFarming":null,"FarmingLocationIds":["u1","u2"]}', '[]'),
                (@neither, 0, @profile, 'Character', 'neither-unit', 'Ascension', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"AscensionFarming":null,"FarmingLocationIds":null}', '[]'),
                (@rank, 0, @profile, 'Character', 'rank-unit', 'Rank', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"AscensionFarming":null,"FarmingLocationIds":["keep-me"]}', '[]'),
                (@nullShardIds, 0, @profile, 'Character', 'null-shard-ids-unit', 'Ascension', 'Active', ARRAY[]::uuid[], now(), now(),
                 '{"AscensionFarming":{"Source":0,"ShardBattleIds":null,"MythicShardBattleIds":["m2"]},"FarmingLocationIds":null}',
                 '[]');
                """;
            command.Parameters.AddWithValue("profile", profileId);
            command.Parameters.AddWithValue("both", bothGoalId);
            command.Parameters.AddWithValue("campaignOnly", campaignOnlyGoalId);
            command.Parameters.AddWithValue("unlockLocations", unlockWithLocationsGoalId);
            command.Parameters.AddWithValue("neither", neitherGoalId);
            command.Parameters.AddWithValue("rank", rankGoalId);
            command.Parameters.AddWithValue("nullShardIds", nullShardIdsGoalId);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using var verifyConnection = new NpgsqlConnection(postgres.GetConnectionString());
        await verifyConnection.OpenAsync(TestContext.Current.CancellationToken);

        // Both -> Campaign (union of shard + mythic ids) + Onslaught; AscensionFarming gone. The
        // `FarmingLocationIds` key stays present with a JSON null value (Ascension never stored shards
        // there) — "null" here means that JSON null, distinct from the key being absent entirely.
        await AssertConfigAsync(verifyConnection, bothGoalId,
            expectedAcquisitionSources: """[{"Ids": ["b1", "b2", "m1"], "Kind": "Campaign"}, {"Ids": [], "Kind": "Onslaught"}]""",
            expectedFarmingLocationIds: "null");

        // Campaign-only -> Campaign keeps its battle ids, no Onslaught entry.
        await AssertConfigAsync(verifyConnection, campaignOnlyGoalId,
            expectedAcquisitionSources: """[{"Ids": ["r1"], "Kind": "Campaign"}]""",
            expectedFarmingLocationIds: "null");

        // Unlock's old FarmingLocationIds move into a Campaign entry and are cleared to JSON null.
        await AssertConfigAsync(verifyConnection, unlockWithLocationsGoalId,
            expectedAcquisitionSources: """[{"Ids": ["u1", "u2"], "Kind": "Campaign"}]""",
            expectedFarmingLocationIds: "null");

        // Neither old field set -> unrestricted campaign.
        await AssertConfigAsync(verifyConnection, neitherGoalId,
            expectedAcquisitionSources: """[{"Ids": [], "Kind": "Campaign"}]""",
            expectedFarmingLocationIds: "null");

        // Rank/Ability goals: untouched FarmingLocationIds, no AcquisitionSources at all.
        await AssertConfigAsync(verifyConnection, rankGoalId,
            expectedAcquisitionSources: null,
            expectedFarmingLocationIds: """["keep-me"]""");

        // A legacy row whose ShardBattleIds is a JSON *null* (not an absent key) must not leak that
        // null into the concatenated Ids array — COALESCE alone doesn't catch a JSON null value, only
        // an absent key (CodeRabbit review of #44): the union must be exactly the other field's ids.
        await AssertConfigAsync(verifyConnection, nullShardIdsGoalId,
            expectedAcquisitionSources: """[{"Ids": ["m2"], "Kind": "Campaign"}]""",
            expectedFarmingLocationIds: "null");

        // The removed key is gone everywhere, including goals that never had it set.
        await using var afCheck = verifyConnection.CreateCommand();
        afCheck.CommandText = "SELECT count(*) FROM goals WHERE config ? 'AscensionFarming';";
        Assert.Equal(0L, await afCheck.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task AssertConfigAsync(
        NpgsqlConnection connection,
        Guid goalId,
        string? expectedAcquisitionSources,
        string? expectedFarmingLocationIds)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT config -> 'AcquisitionSources', config -> 'FarmingLocationIds'
            FROM goals WHERE id = @goal;
            """;
        command.Parameters.AddWithValue("goal", goalId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        var actualAcquisitionSources = await reader.IsDBNullAsync(0, TestContext.Current.CancellationToken)
            ? null
            : reader.GetString(0);
        var actualFarmingLocationIds = await reader.IsDBNullAsync(1, TestContext.Current.CancellationToken)
            ? null
            : reader.GetString(1);

        AssertJsonEquivalent(expectedAcquisitionSources, actualAcquisitionSources);
        AssertJsonEquivalent(expectedFarmingLocationIds, actualFarmingLocationIds);
    }

    /// <summary>Compares two jsonb text fragments by parsed value, not raw text — jsonb's own key/element
    /// ordering (observed as alphabetical-by-key for objects) isn't itself part of what these scenarios
    /// assert.</summary>
    private static void AssertJsonEquivalent(string? expected, string? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected, actual);
            return;
        }

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(expected).RootElement),
            System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(actual).RootElement));
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
}
