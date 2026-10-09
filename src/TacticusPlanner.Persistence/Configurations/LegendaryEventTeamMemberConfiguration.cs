using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class LegendaryEventTeamMemberConfiguration : IEntityTypeConfiguration<LegendaryEventTeamMember>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeamMember> builder)
    {
        builder.ToTable("legendary_event_team_members", table => table.HasCheckConstraint(
            "ck_legendary_event_team_members_position",
            $"(reserve = FALSE AND position BETWEEN 0 AND {LegendaryEventPlanRules.MaxTeamSize - 1}) "
                + "OR (reserve = TRUE AND position = 0)"));
        builder.HasKey(entity => new { entity.TeamId, entity.Reserve, entity.Position });

        builder.Property(entity => entity.TeamId).HasVogenConversion();
        builder.Property(entity => entity.Reserve).IsRequired();
        builder.Property(entity => entity.Position).IsRequired();
        builder.Property(entity => entity.UnitId).HasMaxLength(LegendaryEventPlanRules.MaxUnitIdLength).IsRequired();

        // A unit sits in one slot of a team, members and reserve together (design D6).
        builder.HasIndex(entity => new { entity.TeamId, entity.UnitId })
            .IsUnique()
            .HasDatabaseName("ix_legendary_event_team_members_team_id_unit_id");
    }
}
