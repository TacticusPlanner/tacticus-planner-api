using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class LegendaryEventTeamConfiguration : IEntityTypeConfiguration<LegendaryEventTeam>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeam> builder)
    {
        var lanes = string.Join(", ", LegendaryEventPlanRules.LaneIds.Select(lane => $"'{lane}'"));
        builder.ToTable("legendary_event_teams", table =>
        {
            table.HasCheckConstraint("ck_legendary_event_teams_lane_id", $"lane_id IN ({lanes})");
            table.HasCheckConstraint("ck_legendary_event_teams_sort_order", "sort_order >= 0");
        });
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.PlanId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.LaneId).HasMaxLength(LegendaryEventPlanRules.MaxLaneIdLength).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(LegendaryEventPlanRules.MaxTeamNameLength).IsRequired();
        builder.Property(entity => entity.SortOrder).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // sort_order is deliberately not unique (design D5): the plan revision serialises writers and the
        // service re-densifies a lane on every reorder and delete, so EF never has to permute a unique
        // position column.
        builder.HasIndex(entity => new { entity.PlanId, entity.LaneId })
            .HasDatabaseName("ix_legendary_event_teams_plan_id_lane_id");

        builder
            .HasMany(entity => entity.Members)
            .WithOne(member => member.Team)
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(entity => entity.Objectives)
            .WithOne(objective => objective.Team)
            .HasForeignKey(objective => objective.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(entity => entity.RunDepths)
            .WithOne(depth => depth.Team)
            .HasForeignKey(depth => depth.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
