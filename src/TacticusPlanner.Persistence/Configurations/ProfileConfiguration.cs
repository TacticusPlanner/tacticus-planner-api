using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("profiles");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.AccountId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.DisplayName).IsRequired();
        builder.Property(entity => entity.TacticusUserId);
        builder.Property(entity => entity.TacticusUserIdHash).HasMaxLength(32);

        // HasVogenConversion() only targets the non-nullable struct overload (see GuildConfiguration's
        // ConfiguredByProfileId); ActiveProjectId is a nullable Vogen id, so its converter is written out
        // by hand. No DB-level FK — this is a loose pointer to avoid a Profile<->Project cascade cycle.
        builder.Property(entity => entity.ActiveProjectId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? ProjectId.From(value.Value) : (ProjectId?)null);

        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.HasIndex(entity => entity.AccountId).IsUnique();
        builder
            .HasIndex(entity => entity.TacticusUserIdHash)
            .IsUnique()
            .HasFilter($"{PostgresNaming.SnakeCase(nameof(Profile.TacticusUserIdHash))} IS NOT NULL");
    }
}
