using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goals");
        builder.HasKey(entity => entity.Id);

        // Goal/Project are new Vogen ids that Vogen's cross-assembly EFCore generator does not (yet)
        // discover here — same symptom the codebase already works around for nullable ids (see
        // GuildConfiguration's ConfiguredByProfileId): the converter is written out by hand instead of
        // via HasVogenConversion().
        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => GoalId.From(value))
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasColumnName("profile_id")
            .HasConversion(id => id.Value, value => ProfileId.From(value))
            .IsRequired();
        builder.Property(entity => entity.EntityType).HasColumnName("entity_type").HasConversion<string>().IsRequired();
        builder.Property(entity => entity.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(entity => entity.GoalType).HasColumnName("goal_type").HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Notes).HasColumnName("notes");
        builder.Property(entity => entity.AggregateId).HasColumnName("aggregate_id");
        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // A flat Guid list maps to a native Postgres uuid[] column with no custom converter needed —
        // EF Core's primitive-collection support (8+) handles List<Guid> directly via Npgsql's array
        // type mapping. Reserved for genuinely structured payloads (below) is the jsonb/OwnsX approach.
        builder.Property(entity => entity.DependsOn).HasColumnName("depends_on");

        // Each jsonb payload below is EF Core's JSON owned-entity mapping (OwnsOne/OwnsMany + ToJson()),
        // per ADR 0002/0007 — config/milestones/snapshot/events are kept as separate columns, not merged
        // into one blob, so each concern (target, stages, baseline, history) can evolve independently.
        builder.OwnsOne(entity => entity.Config, config =>
        {
            config.ToJson("config");
            config.OwnsOne(c => c.Rank);
            config.OwnsOne(c => c.Progression);
            config.OwnsOne(c => c.Ability);
            config.OwnsOne(c => c.AscensionFarming);
            config.OwnsOne(c => c.Upgrade, upgrade => upgrade.OwnsMany(u => u.Targets));
            config.OwnsOne(c => c.Equipment);
            config.OwnsOne(c => c.Level);
        });
        builder.OwnsMany(entity => entity.Milestones, milestones => milestones.ToJson("milestones"));
        builder.OwnsOne(entity => entity.Snapshot, snapshot =>
        {
            snapshot.ToJson("snapshot");
            snapshot.OwnsMany(value => value.InitialRequirement);
            snapshot.OwnsMany(value => value.InitialInventoryContribution);
        });
        builder.OwnsMany(entity => entity.Events, events => events.ToJson("events"));

        builder.HasIndex(entity => entity.ProfileId);

        // At most one Active/Paused goal per (profile, entity, goal type) — the app-level checks in
        // CreateGoalEndpoint/CreateCombinedGoalsEndpoint/UpdateGoalStatusEndpoint give the friendly 400,
        // this index is the concurrency backstop that guarantees the invariant even under a race.
        // Completed/Archived/Draft goals are excluded from the filter, so a unit can freely accumulate
        // finished goals of the same type.
        builder.HasIndex(entity => new { entity.ProfileId, entity.EntityType, entity.EntityId, entity.GoalType })
            .IsUnique()
            .HasFilter("status IN ('Active', 'Paused')")
            .HasDatabaseName("ix_goals_one_active_or_paused_per_entity_and_type");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
