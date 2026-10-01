using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        // The order invariant, enforced in the database as a backstop to GoalOrderService: exactly the
        // in-flight (Active/Paused) goals hold a positive position. Uniqueness per profile is the
        // deferrable constraint ix_goals_profile_global_priority (see the AddGlobalGoalPriority migration).
        builder.ToTable("goals", table => table.HasCheckConstraint(
            "ck_goals_global_priority_in_flight",
            "(status IN ('Active', 'Paused') AND global_priority IS NOT NULL AND global_priority > 0) "
                + "OR (status NOT IN ('Active', 'Paused') AND global_priority IS NULL)"));
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
        builder.Property(entity => entity.GlobalPriority);
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
            config.OwnsMany(c => c.AcquisitionSources);
            config.OwnsOne(c => c.Upgrade, upgrade =>
            {
                upgrade.OwnsMany(u => u.Targets);
                upgrade.OwnsOne(u => u.RankRange);
                upgrade.OwnsOne(u => u.ActiveRange);
                upgrade.OwnsOne(u => u.PassiveRange);
            });
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
        builder.OwnsMany(entity => entity.Events, events =>
        {
            events.ToJson("events");
            events.OwnsOne(goalEvent => goalEvent.PreviousTarget, target =>
            {
                target.OwnsMany(value => value.UpgradeTargets);
                target.OwnsOne(value => value.UpgradeRankRange);
                target.OwnsOne(value => value.UpgradeActiveRange);
                target.OwnsOne(value => value.UpgradePassiveRange);
            });
            events.OwnsOne(goalEvent => goalEvent.NewTarget, target =>
            {
                target.OwnsMany(value => value.UpgradeTargets);
                target.OwnsOne(value => value.UpgradeRankRange);
                target.OwnsOne(value => value.UpgradeActiveRange);
                target.OwnsOne(value => value.UpgradePassiveRange);
            });
        });

        builder.HasIndex(entity => entity.ProfileId);

        // Uniqueness of (profile_id, global_priority) is the DEFERRABLE INITIALLY DEFERRED constraint
        // ix_goals_profile_global_priority, created by the AddGlobalGoalPriority migration. It is kept out of
        // the EF model on purpose: a unique index there makes SaveChanges order updates by value and throw a
        // circular-dependency error when a reorder permutes positions. Deferred, the database checks it at
        // commit, so one transaction can permute or compact positions freely (NULLs are distinct, so
        // historical goals never collide).

        // At most one Active/Paused goal per (profile, entity, goal type) — the app-level checks in
        // CreateGoalEndpoint/CreateCombinedGoalsEndpoint/UpdateGoalStatusEndpoint give the friendly 400,
        // this index is the concurrency backstop that guarantees the invariant even under a race.
        // Completed/Archived goals are excluded from the filter, so a unit can freely accumulate
        // finished goals of the same type.
        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
