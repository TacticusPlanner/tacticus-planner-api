using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class ProjectGoalConfiguration : IEntityTypeConfiguration<ProjectGoal>
{
    public void Configure(EntityTypeBuilder<ProjectGoal> builder)
    {
        builder.ToTable("project_goals");
        builder.HasKey(entity => new { entity.ProjectId, entity.GoalId });

        // See GoalConfiguration's comment: Vogen's cross-assembly EFCore generator does not (yet)
        // discover Goal/Project ids, so the converters are written out by hand.
        builder.Property(entity => entity.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => ProjectId.From(value));
        builder.Property(entity => entity.GoalId)
            .HasColumnName("goal_id")
            .HasConversion(id => id.Value, value => GoalId.From(value));
        builder.Property(entity => entity.Priority).HasColumnName("priority").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();

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
