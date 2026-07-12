using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class TacticusIntegrationConfiguration : IEntityTypeConfiguration<TacticusIntegration>
{
    public void Configure(EntityTypeBuilder<TacticusIntegration> builder)
    {
        builder.ToTable("tacticus_integrations");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("profile_id")
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.TacticusApiKey).HasColumnName("tacticus_api_key");
        builder.Property(entity => entity.TacticusSyncLastAttemptedAt).HasColumnName("tacticus_sync_last_attempted_at");
        builder.Property(entity => entity.TacticusSyncLastSucceededAt).HasColumnName("tacticus_sync_last_succeeded_at");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.TacticusIntegration)
            .HasForeignKey<TacticusIntegration>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
