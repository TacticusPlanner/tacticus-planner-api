using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Goals;
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
            $"{PostgresNaming.SnakeCase(nameof(ProjectGoal.Priority))} > 0 AND "
                + $"{PostgresNaming.SnakeCase(nameof(ProjectGoal.Priority))} <= {ProjectValidation.MaxPriority}"
        ));
        builder.HasKey(entity => new { entity.ProjectId, entity.GoalId });

        builder.Property(entity => entity.ProjectId).HasVogenConversion();
        builder.Property(entity => entity.GoalId).HasVogenConversion();
        builder.Property(entity => entity.Priority).IsRequired();
        builder.Property(entity => entity.EntityType).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.EntityId).HasMaxLength(GoalValidation.MaxEntityIdLength).IsRequired();
        builder.Property(entity => entity.GoalType).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.OccupiesInFlightSlot).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();

        builder.HasIndex(entity => new { entity.ProjectId, entity.EntityType, entity.EntityId, entity.GoalType })
            .IsUnique()
            .HasFilter($"{PostgresNaming.SnakeCase(nameof(ProjectGoal.OccupiesInFlightSlot))} = TRUE")
            .HasDatabaseName("ix_project_goals_one_in_flight_slot");

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
