using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Planning;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence.Encryption;

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

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<TacticusIntegration> TacticusIntegrations => Set<TacticusIntegration>();

    public DbSet<PlayerDataSnapshot> PlayerDataSnapshots => Set<PlayerDataSnapshot>();

    public DbSet<PlayerDataOverride> PlayerDataOverrides => Set<PlayerDataOverride>();

    public DbSet<PlanningSettings> PlanningSettings => Set<PlanningSettings>();

    public DbSet<Guild> Guilds => Set<Guild>();

    public DbSet<GuildMember> GuildMembers => Set<GuildMember>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectGoal> ProjectGoals => Set<ProjectGoal>();

    public DbSet<ProjectTeam> ProjectTeams => Set<ProjectTeam>();

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

    private void ApplyEncryptedConverters(ModelBuilder modelBuilder)
    {
        var nullableStringConverter = new ValueConverter<string?, string?>(
            value => columnEncryption.Encrypt(value),
            value => columnEncryption.Decrypt(value)
        );
        var nullableUserIdConverter = new ValueConverter<TacticusUserId?, string?>(
            value => columnEncryption.Encrypt(value.HasValue ? value.Value.Value : null),
            value => value == null ? null : TacticusUserId.From(columnEncryption.Decrypt(value)!)
        );
        var requiredUserIdConverter = new ValueConverter<TacticusUserId, string>(
            value => columnEncryption.Encrypt(value.Value)!,
            value => TacticusUserId.From(columnEncryption.Decrypt(value)!)
        );
        var requiredGuildIdConverter = new ValueConverter<TacticusGuildId, string>(
            value => columnEncryption.Encrypt(value.Value)!,
            value => TacticusGuildId.From(columnEncryption.Decrypt(value)!)
        );

        modelBuilder.Entity<Profile>().Property(entity => entity.TacticusUserId).HasConversion(nullableUserIdConverter);
        modelBuilder.Entity<TacticusIntegration>().Property(entity => entity.TacticusApiKey).HasConversion(nullableStringConverter);
        modelBuilder.Entity<Guild>().Property(entity => entity.TacticusGuildId).HasConversion(requiredGuildIdConverter);
        modelBuilder.Entity<Guild>().Property(entity => entity.GuildApiToken).HasConversion(nullableStringConverter);
        modelBuilder.Entity<GuildMember>().Property(entity => entity.TacticusUserId).HasConversion(requiredUserIdConverter);
    }

    /// <summary>
    /// Global tenant isolation: every profile-owned entity is scoped to <see cref="CurrentProfileId"/> so
    /// callers can't leak another profile's rows through a forgotten <c>.Where</c> (see
    /// https://learn.microsoft.com/en-us/ef/core/querying/filters). Bootstrapping reads that must see a
    /// profile before it is known (first-access provisioning) use <c>IgnoreQueryFilters()</c> explicitly —
    /// see <c>CurrentUserPreProcessor</c> and <c>GetCurrentUserEndpoint</c>.
    ///
    /// <c>Account</c> and the guild entities (<c>Guild</c>, <c>GuildMember</c>) are intentionally excluded:
    /// <c>Account</c> is the identity lookup the current profile is resolved *from*, and guild data is
    /// scoped by guild membership/authorization, not by profile ownership.
    /// </summary>
    private void ApplyProfileQueryFilters(ModelBuilder modelBuilder)
    {
        // ProfileId is the primary key (one row per profile).
        modelBuilder.Entity<Profile>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<TacticusIntegration>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<PlayerDataSnapshot>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<PlayerDataOverride>().HasQueryFilter(entity => entity.Id == CurrentProfileId);
        modelBuilder.Entity<PlanningSettings>().HasQueryFilter(entity => entity.Id == CurrentProfileId);

        // ProfileId is a separate foreign key alongside the entity's own primary key.
        modelBuilder.Entity<Goal>().HasQueryFilter(entity => entity.ProfileId == CurrentProfileId);
        modelBuilder.Entity<Project>().HasQueryFilter(entity => entity.ProfileId == CurrentProfileId);

        // Join tables with no ProfileId of their own: scoped through their parent Project.
        modelBuilder.Entity<ProjectGoal>().HasQueryFilter(entity => entity.Project!.ProfileId == CurrentProfileId);
        modelBuilder.Entity<ProjectTeam>().HasQueryFilter(entity => entity.Project!.ProfileId == CurrentProfileId);
    }
}
