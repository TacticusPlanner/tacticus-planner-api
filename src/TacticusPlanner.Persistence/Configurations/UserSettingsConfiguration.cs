using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.UserSettings;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // Nested JSON value object (see UserSettings.Settings) so future knobs beyond DailyEnergy don't
        // each need their own migration.
        builder.OwnsOne(entity => entity.Settings, settings => settings.ToJson("settings"));

        builder.HasOne(entity => entity.Profile)
            .WithOne(entity => entity.UserSettings)
            .HasForeignKey<UserSettings>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
