using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(entity => entity.Id);

        // See GoalConfiguration's comment: Vogen's cross-assembly EFCore generator does not (yet)
        // discover ProjectId, so the converter is written out by hand.
        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasColumnName("profile_id")
            .HasConversion(id => id.Value, value => ProfileId.From(value))
            .IsRequired();
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.Description).HasColumnName("description");
        builder.Property(entity => entity.Color).HasColumnName("color");
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(entity => entity.IsDefault).HasColumnName("is_default").IsRequired();
        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex([nameof(Project.ProfileId)], "ix_projects_profile_id");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
