using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class LegendaryEventTeamObjectiveConfiguration : IEntityTypeConfiguration<LegendaryEventTeamObjective>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeamObjective> builder)
    {
        builder.ToTable("legendary_event_team_objectives", table => table.HasCheckConstraint(
            "ck_legendary_event_team_objectives_index", "objective_index >= 0"));
        builder.HasKey(entity => new { entity.TeamId, entity.ObjectiveIndex });

        builder.Property(entity => entity.TeamId).HasVogenConversion();
        builder.Property(entity => entity.ObjectiveIndex).IsRequired();
    }
}
