using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProjectTeamConfiguration : IEntityTypeConfiguration<ProjectTeam>
{
    public void Configure(EntityTypeBuilder<ProjectTeam> builder)
    {
        builder.ToTable("project_teams");
        builder.HasKey(entity => new { entity.ProjectId, entity.TeamId });

        // See GoalConfiguration's comment: Vogen's cross-assembly EFCore generator does not (yet)
        // discover ProjectId, so the converter is written out by hand.
        builder.Property(entity => entity.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => ProjectId.From(value));
        builder.Property(entity => entity.TeamId).HasColumnName("team_id");
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();

        builder
            .HasOne(entity => entity.Project)
            .WithMany(entity => entity.ProjectTeams)
            .HasForeignKey(entity => entity.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
