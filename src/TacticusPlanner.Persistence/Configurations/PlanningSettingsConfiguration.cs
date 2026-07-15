using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Planning;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class PlanningSettingsConfiguration : IEntityTypeConfiguration<PlanningSettings>
{
    public void Configure(EntityTypeBuilder<PlanningSettings> builder)
    {
        builder.ToTable("planning_settings");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("profile_id")
            .HasConversion(id => id.Value, value => ProfileId.From(value))
            .ValueGeneratedNever();
        builder.Property(entity => entity.DailyEnergy).HasColumnName("daily_energy").IsRequired();
        builder.Property(entity => entity.Ordering).HasColumnName("ordering").HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne(entity => entity.Profile)
            .WithOne(entity => entity.PlanningSettings)
            .HasForeignKey<PlanningSettings>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
