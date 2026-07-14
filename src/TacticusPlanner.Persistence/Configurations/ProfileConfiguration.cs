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
            .HasColumnName("id")
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.AccountId).HasColumnName("account_id").HasVogenConversion().IsRequired();
        builder.Property(entity => entity.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(entity => entity.TacticusUserId).HasColumnName("tacticus_user_id");
        builder.Property(entity => entity.TacticusUserIdHash).HasColumnName("tacticus_user_id_hash").HasMaxLength(32);

        // See GoalConfiguration's comment: Vogen's cross-assembly EFCore generator does not (yet)
        // discover ProjectId, so the converter is written out by hand. No DB-level FK — this is a loose
        // pointer to avoid a Profile<->Project cascade cycle (see Profile.ActiveProjectId).
        builder.Property(entity => entity.ActiveProjectId)
            .HasColumnName("active_project_id")
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (ProjectId?)null : ProjectId.From(value.Value));

        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(entity => entity.AccountId).IsUnique();
        builder
            .HasIndex(entity => entity.TacticusUserIdHash)
            .IsUnique()
            .HasFilter("tacticus_user_id_hash IS NOT NULL");
    }
}
