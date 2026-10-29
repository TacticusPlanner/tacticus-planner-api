using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Encryption;

/// <summary>
/// One-time, idempotent rewrite of rows still on the legacy encryption envelope: each is decrypted and
/// re-encrypted with the column-bound envelope, and its keyed hash is recomputed with the HKDF hash subkey.
/// A row is migrated exactly when its encrypted column starts with
/// <see cref="AesGcmColumnEncryptionService.CurrentEnvelopePrefix"/>, so a re-run finds nothing to do.
/// </summary>
/// <remarks>
/// Runs in one transaction behind a transaction-scoped advisory lock, so concurrent replicas serialize and
/// the profile and guild-member hashes that link them change together. Mid-transaction unique-index
/// collisions can't happen: old and new hashes come from independent keys, and new hashes stay distinct
/// because the old unique hashes already proved their plaintexts distinct.
/// </remarks>
public static class ColumnEncryptionBackfill
{
    // Arbitrary but fixed: identifies this step among the database's advisory locks.
    private const long AdvisoryLockKey = 7_302_841_455_210_001;

    private const string Legacy = $"NOT LIKE '{AesGcmColumnEncryptionService.CurrentEnvelopePrefix}%'";

    /// <summary>Returns how many rows were rewritten (0 once complete).</summary>
    // ponytail: loads every legacy row in one transaction; batch it if a real dataset ever needs this.
    public static Task<int> RunAsync(PlannerDbContext db, IColumnHashService hashService, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

            var profiles = await db.Profiles
                .FromSqlRaw($"SELECT * FROM profiles WHERE tacticus_user_id {Legacy} FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(ct);
            foreach (var profile in profiles)
            {
                db.Entry(profile).Property(entity => entity.TacticusUserId).IsModified = true;
                profile.TacticusUserIdHash = TacticusUserIdHash.FromNullable(
                    hashService.ComputeHash(profile.TacticusUserId?.Value));
            }

            var integrations = await db.TacticusIntegrations
                .FromSqlRaw($"SELECT * FROM tacticus_integrations WHERE tacticus_api_key {Legacy} FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(ct);
            foreach (var integration in integrations)
            {
                db.Entry(integration).Property(entity => entity.TacticusApiKey).IsModified = true;
            }

            var guilds = await db.Guilds
                .FromSqlRaw(
                    $"SELECT * FROM guilds WHERE tacticus_guild_id {Legacy} OR guild_api_token {Legacy} FOR UPDATE")
                .ToListAsync(ct);
            foreach (var guild in guilds)
            {
                db.Entry(guild).Property(entity => entity.TacticusGuildId).IsModified = true;
                db.Entry(guild).Property(entity => entity.GuildApiToken).IsModified = true;
                guild.TacticusGuildIdHash = TacticusGuildIdHash.FromNullable(
                    hashService.ComputeHash(guild.TacticusGuildId.Value));
            }

            var members = await db.GuildMembers
                .FromSqlRaw($"SELECT * FROM guild_members WHERE tacticus_user_id {Legacy} FOR UPDATE")
                .ToListAsync(ct);
            foreach (var member in members)
            {
                db.Entry(member).Property(entity => entity.TacticusUserId).IsModified = true;
                member.TacticusUserIdHash = TacticusUserIdHash.FromNullable(
                    hashService.ComputeHash(member.TacticusUserId.Value));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return profiles.Count + integrations.Count + guilds.Count + members.Count;
        });
}
