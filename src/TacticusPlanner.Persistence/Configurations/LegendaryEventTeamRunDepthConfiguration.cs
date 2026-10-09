using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class LegendaryEventTeamRunDepthConfiguration : IEntityTypeConfiguration<LegendaryEventTeamRunDepth>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeamRunDepth> builder)
    {
        var sources = string.Join(", ", Enum.GetNames<LegendaryEventDepthSource>().Select(name => $"'{name}'"));
        builder.ToTable("legendary_event_team_run_depths", table =>
        {
            table.HasCheckConstraint(
                "ck_legendary_event_team_run_depths_run",
                $"run BETWEEN {LegendaryEventPlanRules.MinRun} AND {LegendaryEventPlanRules.MaxRun}");
            table.HasCheckConstraint("ck_legendary_event_team_run_depths_depth", "expected_battle_clears >= 1");
            table.HasCheckConstraint("ck_legendary_event_team_run_depths_source", $"source IN ({sources})");
        });
        builder.HasKey(entity => new { entity.TeamId, entity.Run });

        builder.Property(entity => entity.TeamId).HasVogenConversion();
        builder.Property(entity => entity.Run).IsRequired();
        builder.Property(entity => entity.ExpectedBattleClears).IsRequired();
        builder.Property(entity => entity.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.RecordedAt).IsRequired();
    }
}
