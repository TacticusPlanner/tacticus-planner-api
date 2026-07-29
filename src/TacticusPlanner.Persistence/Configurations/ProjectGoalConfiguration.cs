using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProjectGoalConfiguration : IEntityTypeConfiguration<ProjectGoal>
{
    /// <summary>Mirrors the API's <c>CreateGoalValidator</c>/<c>CreateCombinedGoalsValidator</c>
    /// FluentValidation rule — a DB-level backstop, not the primary enforcement point.</summary>
    public const int MaxPriority = 10000;

    public void Configure(EntityTypeBuilder<ProjectGoal> builder)
    {
        builder.ToTable("project_goals", table => table.HasCheckConstraint(
            "ck_project_goals_priority_range",
            $"priority > 0 AND priority <= {MaxPriority}"
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
