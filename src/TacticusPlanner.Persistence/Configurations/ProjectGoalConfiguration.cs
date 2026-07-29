using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProjectGoalConfiguration : IEntityTypeConfiguration<ProjectGoal>
{
    public void Configure(EntityTypeBuilder<ProjectGoal> builder)
    {
        // A DB-level backstop, not the primary enforcement point — see ProjectValidation.MaxPriority for
        // the shared value the API's FluentValidation rule also sources.
        builder.ToTable("project_goals", table => table.HasCheckConstraint(
            "ck_project_goals_priority_range",
            $"priority > 0 AND priority <= {ProjectValidation.MaxPriority}"
        ));
        builder.HasKey(entity => new { entity.ProjectId, entity.GoalId });

        builder.Property(entity => entity.ProjectId).HasVogenConversion();
        builder.Property(entity => entity.GoalId).HasVogenConversion();
        builder.Property(entity => entity.Priority).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();

        builder
            .HasOne(entity => entity.Project)
            .WithMany(entity => entity.ProjectGoals)
            .HasForeignKey(entity => entity.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(entity => entity.Goal)
            .WithMany()
            .HasForeignKey(entity => entity.GoalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
