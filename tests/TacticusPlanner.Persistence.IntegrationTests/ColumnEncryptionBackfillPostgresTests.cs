using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

public sealed class ColumnEncryptionBackfillPostgresTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    private static readonly IOptions<ColumnEncryptionOptions> EncryptionOptions = Options.Create(new ColumnEncryptionOptions
    {
        CurrentKeyVersion = "v1",
        Keys = { ["v1"] = Convert.ToBase64String(Key) },
    });

    private readonly HmacColumnHashService hashService = new(EncryptionOptions);

    [Fact]
    public async Task BackfillRewritesLegacyRowsOnceAndKeepsMembersLinked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        await using var db = CreateContext(postgres.GetConnectionString());
        await db.Database.MigrateAsync(ct);

        var profileId = ProfileId.From(Guid.CreateVersion7());
        var guildId = GuildId.From(Guid.CreateVersion7());
        db.Profiles.Add(new Profile
        {
            Id = profileId,
            Issuer = "test",
            Subject = profileId.Value.ToString(),
            DisplayName = "Test",
            TacticusUserId = TacticusUserId.From("user-1"),
        });
        db.TacticusIntegrations.Add(new TacticusIntegration { Id = profileId, TacticusApiKey = "api-key" });
        db.Guilds.Add(new Guild
        {
            Id = guildId,
            TacticusGuildId = TacticusGuildId.From("guild-1"),
            Tag = "BACKFILL",
            Name = "Backfill",
            GuildApiToken = "guild-token",
        });
        db.GuildMembers.Add(new GuildMember
        {
            Id = GuildMemberId.From(Guid.CreateVersion7()),
            GuildId = guildId,
            TacticusUserId = TacticusUserId.From("user-1"),
            ProfileId = profileId,
        });
        await db.SaveChangesAsync(ct);

        // Rewind every row to what the previous release wrote: the AAD-less envelope and an HMAC keyed with
        // the encryption key itself.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE profiles SET tacticus_user_id = {LegacyEncrypt("user-1")}, tacticus_user_id_hash = {LegacyHash("user-1")}",
            ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE tacticus_integrations SET tacticus_api_key = {LegacyEncrypt("api-key")}", ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE guilds SET tacticus_guild_id = {LegacyEncrypt("guild-1")}, tacticus_guild_id_hash = {LegacyHash("guild-1")}, guild_api_token = {LegacyEncrypt("guild-token")}",
            ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE guild_members SET tacticus_user_id = {LegacyEncrypt("user-1")}, tacticus_user_id_hash = {LegacyHash("user-1")}",
            ct);

        Assert.Equal(4, await ColumnEncryptionBackfill.RunAsync(db, hashService, ct));

        var envelopes = await ReadEnvelopesAsync(db, ct);
        Assert.All(envelopes, envelope =>
            Assert.StartsWith(AesGcmColumnEncryptionService.CurrentEnvelopePrefix, envelope, StringComparison.Ordinal));

        var profile = await db.Profiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(ct);
        var integration = await db.TacticusIntegrations.IgnoreQueryFilters().AsNoTracking().SingleAsync(ct);
        var guild = await db.Guilds.AsNoTracking().SingleAsync(ct);
        var member = await db.GuildMembers.AsNoTracking().SingleAsync(ct);
        Assert.Equal("user-1", profile.TacticusUserId?.Value);
        Assert.Equal("api-key", integration.TacticusApiKey);
        Assert.Equal("guild-1", guild.TacticusGuildId.Value);
        Assert.Equal("guild-token", guild.GuildApiToken);
        Assert.Equal("user-1", member.TacticusUserId.Value);

        var newUserHash = TacticusUserIdHash.From(hashService.ComputeHash("user-1")!);
        Assert.Equal(newUserHash, profile.TacticusUserIdHash);
        Assert.Equal(newUserHash, member.TacticusUserIdHash);
        Assert.Equal(TacticusGuildIdHash.From(hashService.ComputeHash("guild-1")!), guild.TacticusGuildIdHash);
        // Guild sync links members to profiles by this lookup.
        Assert.True(await db.Profiles.IgnoreQueryFilters()
            .AnyAsync(entity => entity.TacticusUserIdHash == member.TacticusUserIdHash, ct));

        Assert.Equal(0, await ColumnEncryptionBackfill.RunAsync(db, hashService, ct));
        Assert.Equal(envelopes, await ReadEnvelopesAsync(db, ct));
    }

    [Fact]
    public async Task HashSubkeyMigrationDeletesRaidObservationsButKeepsGuilds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        await using var db = CreateContext(postgres.GetConnectionString());
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021204713_InitialCreate", cancellationToken: ct);

        var guild = new Guild
        {
            Id = GuildId.From(Guid.CreateVersion7()),
            TacticusGuildId = TacticusGuildId.From("guild-1"),
            Tag = "RAIDS",
            Name = "Raids",
        };
        var season = new GuildRaidSeason
        {
            Id = GuildRaidSeasonId.From(Guid.CreateVersion7()),
            GuildId = guild.Id,
            SeasonNumber = 112,
            SeasonConfigId = "season",
            ObservedAt = DateTimeOffset.UtcNow,
        };
        season.Attacks.Add(new GuildRaidAttack
        {
            Id = GuildRaidAttackId.From(Guid.CreateVersion7()),
            GuildRaidSeasonId = season.Id,
            ContentHash = "content",
            TacticusUserIdHash = TacticusUserIdHash.From(LegacyHash("user-1")),
            EncounterType = GuildRaidEncounterType.Boss,
            UnitSetId = "boss",
            Difficulty = GuildRaidDifficulty.Common,
            DamageType = GuildRaidDamageType.Battle,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        db.AddRange(guild, season, new GuildRaidSyncState
        {
            GuildId = guild.Id,
            State = GuildRaidObservationState.Active,
            ObservedAt = season.ObservedAt,
            LastAttemptedAt = season.ObservedAt,
            ActiveSeasonId = season.Id,
        });
        await db.SaveChangesAsync(ct);

        await migrator.MigrateAsync(cancellationToken: ct);

        Assert.False(await db.GuildRaidSyncStates.AnyAsync(ct));
        Assert.False(await db.GuildRaidSeasons.AnyAsync(ct));
        Assert.False(await db.GuildRaidAttacks.AnyAsync(ct));
        Assert.True(await db.Guilds.AnyAsync(ct));
    }

    // EF caches the model, and with it the converters' captured encryption service, per internal service
    // provider. Other tests in this assembly use passthrough encryption, so this one gets its own provider.
    private static PlannerDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString, options => options.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new EntityMetadataInterceptor(TimeProvider.System))
            .EnableServiceProviderCaching(false)
            .Options,
        new AesGcmColumnEncryptionService(EncryptionOptions),
        new NoProfile());

    private static Task<List<string>> ReadEnvelopesAsync(PlannerDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<string>(
            """
            SELECT tacticus_user_id AS "Value" FROM profiles
            UNION ALL SELECT tacticus_api_key FROM tacticus_integrations
            UNION ALL SELECT tacticus_guild_id FROM guilds
            UNION ALL SELECT guild_api_token FROM guilds
            UNION ALL SELECT tacticus_user_id FROM guild_members
            """).ToListAsync(ct);

    private static string LegacyEncrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key, 16);
        aes.Encrypt(nonce, bytes, ciphertext, tag);
        return string.Join(':', "tpenc", "v1", Base64Url(nonce), Base64Url(tag), Base64Url(ciphertext));
    }

    private static byte[] LegacyHash(string value) => HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(value));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class NoProfile : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => null;
    }
}
