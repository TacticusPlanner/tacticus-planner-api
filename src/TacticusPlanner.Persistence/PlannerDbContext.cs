using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Persistence;

public sealed class PlannerDbContext(
    DbContextOptions<PlannerDbContext> options,
    IColumnEncryptionService columnEncryption
) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<TacticusIntegration> TacticusIntegrations => Set<TacticusIntegration>();

    public DbSet<PlayerDataSnapshot> PlayerDataSnapshots => Set<PlayerDataSnapshot>();

    public DbSet<PlayerDataOverride> PlayerDataOverrides => Set<PlayerDataOverride>();

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
}
