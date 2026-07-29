using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(ProjectValidation.MaxNameLength).IsRequired();
        builder.Property(entity => entity.Description).HasMaxLength(ProjectValidation.MaxDescriptionLength);
        builder.Property(entity => entity.Color).HasMaxLength(ProjectValidation.MaxColorLength);
        builder.Property(entity => entity.Status).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Type).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.HasIndex([nameof(Project.ProfileId)], "ix_projects_profile_id");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
