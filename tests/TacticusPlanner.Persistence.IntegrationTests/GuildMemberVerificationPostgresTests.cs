using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;
using Testcontainers.PostgreSql;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>Schema rules of guild players and member claims (<c>guild-member-verification</c>).</summary>
public sealed class GuildMemberVerificationPostgresTests
{
    private static readonly GuildId Guild = GuildId.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    [Fact]
    public async Task UpgradeKeepsMembersUnclaimedAndPlayersAreUniqueAndGoWithTheGuild()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = await StartAsync(ct);
        await using var db = Context(postgres);
        await db.GetService<IMigrator>().MigrateAsync("AddGuildWars", ct);

        // Raw SQL: the model already has the columns this migration adds.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO guilds (id, tacticus_guild_id, tag, name, level, revision, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'guild', 'TAG', 'Guild', 1, 0, now(), now());
            INSERT INTO guild_members (id, revision, guild_id, tacticus_user_id, tacticus_user_id_hash, role, level,
                last_synced_at, created_at, updated_at)
            VALUES ('00000000-0000-0000-0000-000000000002', 0, '00000000-0000-0000-0000-000000000001', 'member',
                decode(repeat('02', 32), 'hex'), 'Leader', 10, now(), now(), now());
            """,
            ct);
        await db.Database.MigrateAsync(ct);

        var member = await db.GuildMembers.AsNoTracking().SingleAsync(ct);
        Assert.Equal((null, null, null, null),
            (member.VerifiedProfileId, member.VerificationMethod, member.VerifiedAt, member.RejectedProfileId));

        var hash = TacticusUserIdHash.From(Enumerable.Repeat((byte)2, 32).ToArray());
        db.GuildPlayers.Add(Player(hash));
        await db.SaveChangesAsync(ct);
        db.GuildPlayers.Add(Player(hash));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        db.ChangeTracker.Clear();

        await db.Guilds.Where(guild => guild.Id == Guild).ExecuteDeleteAsync(ct);
        Assert.Equal(0, await db.GuildPlayers.CountAsync(ct));
    }

    [Fact]
    public async Task DeletingAProfileLeavesItsClaimUnverified()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = await StartAsync(ct);
        await using var db = Context(postgres);
        await db.Database.MigrateAsync(ct);
        var profile = new Profile { Id = ProfileId.From(Guid.NewGuid()), Issuer = "test", Subject = "claim", DisplayName = "Claimant" };
        db.Profiles.Add(profile);
        db.Guilds.Add(new Guild { Id = Guild, TacticusGuildId = TacticusGuildId.From("guild"), Tag = "TAG", Name = "Guild" });
        var member = new GuildMember
        {
            Id = GuildMemberId.From(Guid.NewGuid()),
            GuildId = Guild,
            TacticusUserId = TacticusUserId.From("member"),
            ProfileId = profile.Id,
            Role = GuildRole.Leader,
        };
        GuildClaimEvaluator.Verify(member, GuildClaimVerificationMethod.Key, DateTimeOffset.UtcNow);
        db.GuildMembers.Add(member);
        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await db.Profiles.IgnoreQueryFilters().Where(entity => entity.Id == profile.Id).ExecuteDeleteAsync(ct));

        var stored = await db.GuildMembers.AsNoTracking().SingleAsync(ct);
        Assert.Null(stored.ProfileId);
        Assert.Equal(GuildClaimState.None, GuildClaimEvaluator.StateOf(stored));
    }

    [Fact]
    public async Task ConcurrentRecordingAddsEachPlayerOnceAndFillsAMissingSuffix()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = await StartAsync(ct);
        await using (var setup = Context(postgres))
        {
            await setup.Database.MigrateAsync(ct);
            setup.Guilds.Add(new Guild { Id = Guild, TacticusGuildId = TacticusGuildId.From("guild"), Tag = "TAG", Name = "Guild" });
            await setup.SaveChangesAsync(ct);
        }

        var players = Enumerable.Range(1, 30)
            .Select(index => TacticusUserIdHash.From(Enumerable.Repeat((byte)index, 32).ToArray()))
            .ToList();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async run =>
        {
            await using var db = Context(postgres);
            // Half the runs know no suffix, as a roster seen before suffixes were kept.
            await new GuildPlayerRegistry(db, TimeProvider.System).AddAsync(
                Guild, players.Select(hash => (hash, run % 2 == 0 ? (string?)null : $"{hash.Value[0]:x4}")), ct);
        }));

        await using var verify = Context(postgres);
        var stored = await verify.GuildPlayers.AsNoTracking().ToListAsync(ct);
        Assert.Equal(30, stored.Count);
        Assert.All(stored, player => Assert.Equal($"{player.TacticusUserIdHash.Value[0]:x4}", player.TacticusUserIdSuffix));
    }

    private static async Task<PostgreSqlContainer> StartAsync(CancellationToken ct)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(ct);
        return postgres;
    }

    private static PlannerDbContext Context(PostgreSqlContainer postgres) => new(
        new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options,
        new PassthroughEncryption(),
        new NoProfile());

    private static GuildPlayer Player(TacticusUserIdHash hash) => new()
    {
        Id = GuildPlayerId.From(Guid.CreateVersion7()),
        GuildId = Guild,
        TacticusUserIdHash = hash,
    };

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext, string column) => plaintext;
        public string? Decrypt(string? envelope, string column) => envelope;
    }

    private sealed class NoProfile : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => null;
    }
}
