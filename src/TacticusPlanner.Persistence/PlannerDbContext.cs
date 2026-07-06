using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Users;
using TacticusPlanner.Persistence.Users.PlayerData;

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
        var converter = new ValueConverter<string?, string?>(
            value => columnEncryption.Encrypt(value),
            value => columnEncryption.Decrypt(value)
        );

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            foreach (var property in clrType.GetProperties())
            {
                if (property.PropertyType != typeof(string)
                    || !Attribute.IsDefined(property, typeof(EncryptedAttribute)))
                {
                    continue;
                }

                modelBuilder
                    .Entity(clrType)
                    .Property(property.Name)
                    .HasConversion(converter);
            }
        }
    }
}
