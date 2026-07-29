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
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.TacticusApiKey);
        builder.Property(entity => entity.TacticusSyncLastAttemptedAt);
        builder.Property(entity => entity.TacticusSyncLastSucceededAt);
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.TacticusIntegration)
            .HasForeignKey<TacticusIntegration>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
