using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goals");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.EntityType).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.EntityId).HasMaxLength(GoalValidation.MaxEntityIdLength).IsRequired();
        builder.Property(entity => entity.GoalType).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Status).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Notes).HasMaxLength(GoalValidation.MaxNotesLength);
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // A flat Guid list maps to a native Postgres uuid[] column with no custom converter needed —
        // EF Core's primitive-collection support (8+) handles List<Guid> directly via Npgsql's array
        // type mapping. Reserved for genuinely structured payloads (below) is the jsonb/OwnsX approach.
        builder.Property(entity => entity.DependsOn);

        // Each jsonb payload below is EF Core's JSON owned-entity mapping (OwnsOne/OwnsMany + ToJson()),
        // per ADR 0002/0007 — config/snapshot/events are kept as separate columns, not merged into one
        // blob, so each concern (target, baseline, history) can evolve independently. Staying on
        // OwnsOne/OwnsMany rather than ComplexProperty/ComplexCollection — see the Events comment below.
        builder.OwnsOne(entity => entity.Config, config =>
        {
            config.ToJson("config");
            config.OwnsOne(c => c.Rank);
            config.OwnsOne(c => c.Progression);
            config.OwnsOne(c => c.Ability);
            config.OwnsOne(c => c.AscensionFarming);
            config.OwnsOne(c => c.Upgrade, upgrade => upgrade.OwnsMany(u => u.Targets));
            config.OwnsOne(c => c.Item);
            config.OwnsOne(c => c.Level);
        });
        builder.OwnsOne(entity => entity.Snapshot, snapshot =>
        {
            snapshot.ToJson("snapshot");
            snapshot.OwnsMany(value => value.InitialRequirement);
            snapshot.OwnsMany(value => value.InitialInventoryContribution);
        });
        // Tried ComplexCollection(...).ToJson() (EF Core 10's OwnsMany replacement) here — it compiles but
        // throws at model-build/query time against this stack (confirmed empirically: every endpoint
        // touching a Goal 500s). Staying on OwnsMany().ToJson() until that's fixed upstream.
        builder.OwnsMany(entity => entity.Events, events => events.ToJson("events"));

        builder.HasIndex(entity => entity.ProfileId);

        // At most one Active/Paused goal per (profile, entity, goal type) — the app-level checks in
        // CreateGoalEndpoint/CreateCombinedGoalsEndpoint/UpdateGoalStatusEndpoint give the friendly 400,
        // this index is the concurrency backstop that guarantees the invariant even under a race.
        // Completed/Archived goals are excluded from the filter, so a unit can freely accumulate
        // finished goals of the same type.
        builder.HasIndex(entity => new { entity.ProfileId, entity.EntityType, entity.EntityId, entity.GoalType })
            .IsUnique()
            .HasFilter($"{PostgresNaming.SnakeCase(nameof(Goal.Status))} IN ('Active', 'Paused')")
            .HasDatabaseName("ix_goals_one_active_or_paused_per_entity_and_type");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
