using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence.Encryption;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Persistence;

public sealed class PlannerDbContext(
    DbContextOptions<PlannerDbContext> options,
    IColumnEncryptionService columnEncryption,
    ICurrentProfileProvider currentProfile
) : DbContext(options)
{
    /// <summary>Backs every profile-scoped <c>HasQueryFilter</c> below. A property (not a field) so the
    /// filter lambdas capture <c>this</c> and re-evaluate it per query — required because this context is
    /// pooled (see <see cref="ICurrentProfileProvider"/>) and reused across requests/profiles.</summary>
    private ProfileId? CurrentProfileId => currentProfile.ProfileId;

    /// <summary>Locks the current profile's row (<c>FOR NO KEY UPDATE</c>: its key is never written, so
    /// inserts of rows that reference it are not blocked) and returns it tracked, in one statement. Must run
    /// inside a transaction.</summary>
    public Task<Profile> LockCurrentProfileAsync(CancellationToken ct) =>
        Profiles.FromSqlRaw("SELECT * FROM profiles WHERE id = {0} FOR NO KEY UPDATE", CurrentProfileId!.Value.Value)
            .SingleAsync(ct);

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<TacticusIntegration> TacticusIntegrations => Set<TacticusIntegration>();

    public DbSet<PlayerDataSnapshot> PlayerDataSnapshots => Set<PlayerDataSnapshot>();

    public DbSet<PlayerDataOverride> PlayerDataOverrides => Set<PlayerDataOverride>();

    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();

    public DbSet<Guild> Guilds => Set<Guild>();

    public DbSet<GuildMember> GuildMembers => Set<GuildMember>();

    public DbSet<GuildRaidSyncState> GuildRaidSyncStates => Set<GuildRaidSyncState>();

    public DbSet<GuildRaidSeason> GuildRaidSeasons => Set<GuildRaidSeason>();

    public DbSet<GuildRaidAttack> GuildRaidAttacks => Set<GuildRaidAttack>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectGoal> ProjectGoals => Set<ProjectGoal>();

    public DbSet<LegendaryEventPlan> LegendaryEventPlans => Set<LegendaryEventPlan>();

    public DbSet<LegendaryEventRunRecord> LegendaryEventRunRecords => Set<LegendaryEventRunRecord>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlannerDbContext).Assembly);
        ApplyEncryptedConverters(modelBuilder);
        ApplyProfileQueryFilters(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.RegisterAllInVogenEfCoreConverters();
    }

    /// <summary>Each encrypted column passes its own <c>table.column</c> name, which the encryption service
    /// binds into the ciphertext (see <see cref="AesGcmColumnEncryptionService"/>).</summary>
    private void ApplyEncryptedConverters(ModelBuilder modelBuilder)
    {
        ValueConverter<string?, string?> NullableString(string column) => new(
            value => columnEncryption.Encrypt(value, column),
            value => columnEncryption.Decrypt(value, column)
        );
        ValueConverter<TacticusUserId, string> RequiredUserId(string column) => new(
            value => columnEncryption.Encrypt(value.Value, column)!,
            value => TacticusUserId.From(columnEncryption.Decrypt(value, column)!)
        );
        var nullableUserIdConverter = new ValueConverter<TacticusUserId?, string?>(
            value => columnEncryption.Encrypt(value.HasValue ? value.Value.Value : null, "profiles.tacticus_user_id"),
            value => value == null ? null : TacticusUserId.From(columnEncryption.Decrypt(value, "profiles.tacticus_user_id")!)
        );
        var requiredGuildIdConverter = new ValueConverter<TacticusGuildId, string>(
            value => columnEncryption.Encrypt(value.Value, "guilds.tacticus_guild_id")!,
            value => TacticusGuildId.From(columnEncryption.Decrypt(value, "guilds.tacticus_guild_id")!)
        );

        modelBuilder.Entity<Profile>().Property(entity => entity.TacticusUserId).HasConversion(nullableUserIdConverter);
        modelBuilder.Entity<TacticusIntegration>().Property(entity => entity.TacticusApiKey)
            .HasConversion(NullableString("tacticus_integrations.tacticus_api_key"));
        modelBuilder.Entity<Guild>().Property(entity => entity.TacticusGuildId).HasConversion(requiredGuildIdConverter);
        modelBuilder.Entity<Guild>().Property(entity => entity.GuildApiToken).HasConversion(NullableString("guilds.guild_api_token"));
        modelBuilder.Entity<GuildMember>().Property(entity => entity.TacticusUserId)
            .HasConversion(RequiredUserId("guild_members.tacticus_user_id"));
    }

    /// <summary>
    /// Global tenant isolation: every profile-owned entity is scoped to <see cref="CurrentProfileId"/> so
    /// callers can't leak another profile's rows through a forgotten <c>.Where</c> (see
    /// https://learn.microsoft.com/en-us/ef/core/querying/filters). Bootstrapping reads that must see a
    /// profile before it is known (first-access provisioning) use <c>IgnoreQueryFilters()</c> explicitly —
    /// see <c>CurrentUserPreProcessor</c> and <c>GetCurrentUserEndpoint</c>.
    ///
    /// The guild entities (<c>Guild</c>, <c>GuildMember</c>) are intentionally excluded: guild data is
    /// scoped by guild membership/authorization, not by profile ownership. The identity lookup that resolves
    /// the current profile (by issuer/subject) runs with <c>IgnoreQueryFilters()</c>.
    /// </summary>
    private void ApplyProfileQueryFilters(ModelBuilder modelBuilder)
    {
        // ProfileId is the primary key (one row per profile).
        modelBuilder.Entity<Profile>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<TacticusIntegration>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<PlayerDataSnapshot>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<PlayerDataOverride>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<UserSettingsEntity>().HasQueryFilter(entity => entity.Id == CurrentProfileId);

        // ProfileId is a separate foreign key alongside the entity's own primary key.
        modelBuilder.Entity<Goal>().HasQueryFilter(entity => entity.ProfileId == CurrentProfileId);
        modelBuilder.Entity<Project>().HasQueryFilter(entity => entity.ProfileId == CurrentProfileId);
        modelBuilder.Entity<LegendaryEventPlan>().HasQueryFilter(entity => entity.ProfileId == CurrentProfileId);
        modelBuilder.Entity<LegendaryEventRunRecord>().HasQueryFilter(entity => entity.ProfileId == CurrentProfileId);

        // Join table with no ProfileId of its own: scoped through its parent Project.
        modelBuilder.Entity<ProjectGoal>().HasQueryFilter(entity => entity.Project!.ProfileId == CurrentProfileId);
    }
}
