using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Legendary Event plans and their teams (LRE Stage 2). Tables live in <c>public</c> with a
/// <c>legendary_event_</c> prefix like every other table; cascades run profile → plan → team →
/// member/objective/run depth so account purge removes everything.
/// </summary>
public sealed class LegendaryEventPlanConfiguration : IEntityTypeConfiguration<LegendaryEventPlan>
{
    public void Configure(EntityTypeBuilder<LegendaryEventPlan> builder)
    {
        builder.ToTable("legendary_event_plans");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.EventId).HasMaxLength(LegendaryEventValidation.MaxEventIdLength).IsRequired();
        builder.Property(entity => entity.CatalogVersion)
            .HasMaxLength(LegendaryEventValidation.MaxCatalogVersionLength)
            .IsRequired();
        builder.Property(entity => entity.Notes).HasMaxLength(LegendaryEventValidation.MaxNotesLength);
        builder.Property(entity => entity.ShowPaidOptions).IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // One plan per (profile, event); also the backstop when two callers lazily create the same plan.
        builder.HasIndex(entity => new { entity.ProfileId, entity.EventId })
            .IsUnique()
            .HasDatabaseName("ix_legendary_event_plans_profile_id_event_id");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LegendaryEventTeamConfiguration : IEntityTypeConfiguration<LegendaryEventTeam>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeam> builder)
    {
        var laneId = PostgresNaming.SnakeCase(nameof(LegendaryEventTeam.LaneId));
        var lanes = string.Join(", ", LegendaryEventValidation.LaneIds.Select(lane => $"'{lane}'"));
        builder.ToTable("legendary_event_teams", table =>
        {
            table.HasCheckConstraint("ck_legendary_event_teams_lane_id", $"{laneId} IN ({lanes})");
            table.HasCheckConstraint(
                "ck_legendary_event_teams_sort_order",
                $"{PostgresNaming.SnakeCase(nameof(LegendaryEventTeam.SortOrder))} >= 0");
        });
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.PlanId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.LaneId).HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(LegendaryEventValidation.MaxTeamNameLength).IsRequired();
        builder.Property(entity => entity.SortOrder).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // No unique index on (plan, lane, sort_order): EF cannot permute a unique position column, and the
        // plan revision already serialises writers (design D5).
        builder.HasIndex(entity => new { entity.PlanId, entity.LaneId })
            .HasDatabaseName("ix_legendary_event_teams_plan_id_lane_id");

        builder
            .HasOne(entity => entity.Plan)
            .WithMany(plan => plan.Teams)
            .HasForeignKey(entity => entity.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LegendaryEventTeamMemberConfiguration : IEntityTypeConfiguration<LegendaryEventTeamMember>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeamMember> builder)
    {
        var reserve = PostgresNaming.SnakeCase(nameof(LegendaryEventTeamMember.Reserve));
        var position = PostgresNaming.SnakeCase(nameof(LegendaryEventTeamMember.Position));
        builder.ToTable("legendary_event_team_members", table => table.HasCheckConstraint(
            "ck_legendary_event_team_members_position",
            $"({reserve} = FALSE AND {position} BETWEEN 0 AND {LegendaryEventValidation.MaxMembers - 1}) "
                + $"OR ({reserve} = TRUE AND {position} = 0)"));
        builder.HasKey(entity => new { entity.TeamId, entity.Reserve, entity.Position });

        builder.Property(entity => entity.TeamId).HasVogenConversion();
        builder.Property(entity => entity.UnitId).HasMaxLength(LegendaryEventValidation.MaxUnitIdLength).IsRequired();

        builder.HasIndex(entity => new { entity.TeamId, entity.UnitId })
            .IsUnique()
            .HasDatabaseName("ix_legendary_event_team_members_team_id_unit_id");

        builder
            .HasOne(entity => entity.Team)
            .WithMany(team => team.Members)
            .HasForeignKey(entity => entity.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LegendaryEventTeamObjectiveConfiguration : IEntityTypeConfiguration<LegendaryEventTeamObjective>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeamObjective> builder)
    {
        builder.ToTable("legendary_event_team_objectives", table => table.HasCheckConstraint(
            "ck_legendary_event_team_objectives_objective_index",
            $"{PostgresNaming.SnakeCase(nameof(LegendaryEventTeamObjective.ObjectiveIndex))} >= 0"));
        builder.HasKey(entity => new { entity.TeamId, entity.ObjectiveIndex });

        builder.Property(entity => entity.TeamId).HasVogenConversion();

        builder
            .HasOne(entity => entity.Team)
            .WithMany(team => team.Objectives)
            .HasForeignKey(entity => entity.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LegendaryEventTeamRunDepthConfiguration : IEntityTypeConfiguration<LegendaryEventTeamRunDepth>
{
    public void Configure(EntityTypeBuilder<LegendaryEventTeamRunDepth> builder)
    {
        var run = PostgresNaming.SnakeCase(nameof(LegendaryEventTeamRunDepth.Run));
        var depth = PostgresNaming.SnakeCase(nameof(LegendaryEventTeamRunDepth.ExpectedBattleClears));
        var source = PostgresNaming.SnakeCase(nameof(LegendaryEventTeamRunDepth.Source));
        builder.ToTable("legendary_event_team_run_depths", table =>
        {
            table.HasCheckConstraint(
                "ck_legendary_event_team_run_depths_run",
                $"{run} BETWEEN {LegendaryEventValidation.MinRun} AND {LegendaryEventValidation.MaxRun}");
            table.HasCheckConstraint("ck_legendary_event_team_run_depths_depth", $"{depth} >= 1");
            table.HasCheckConstraint(
                "ck_legendary_event_team_run_depths_source",
                $"{source} IN ('{LegendaryEventDepthSource.Estimate}', '{LegendaryEventDepthSource.Manual}')");
        });
        builder.HasKey(entity => new { entity.TeamId, entity.Run });

        builder.Property(entity => entity.TeamId).HasVogenConversion();
        builder.Property(entity => entity.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.RecordedAt).IsRequired();

        builder
            .HasOne(entity => entity.Team)
            .WithMany(team => team.RunDepths)
            .HasForeignKey(entity => entity.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
