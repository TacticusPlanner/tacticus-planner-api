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
        // deferrable constraint ix_goals_profile_global_priority (raw SQL in the InitialCreate migration).
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
        // per ADR 0002/0007 — config/snapshot are kept as separate columns, not merged into one blob, so
        // each concern (target, baseline) can evolve independently. Tried ComplexCollection(...).ToJson()
        // (EF Core 10's OwnsMany replacement) — it compiles but throws at model-build/query time against
        // this stack (every endpoint touching a Goal 500s). Staying on OwnsOne/OwnsMany().ToJson() until
        // that's fixed upstream.
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
        builder.HasIndex(entity => entity.ProfileId);

        // Uniqueness of (profile_id, global_priority) is the DEFERRABLE INITIALLY DEFERRED constraint
        // ix_goals_profile_global_priority, appended as raw SQL to the InitialCreate migration and guarded by
        // a Persistence integration test so a future squash cannot lose it. It is kept out of
        // the EF model on purpose: a unique index there makes SaveChanges order updates by value and throw a
        // circular-dependency error when a reorder permutes positions. Deferred, the database checks it at
        // commit, so one transaction can permute or compact positions freely (NULLs are distinct, so
        // historical goals never collide).

        // The in-flight slot rule (one Active/Paused goal per unit and type; per normalized end target for
        // Rank) has no index: ProjectGoalPlanningService.FindConflictAsync enforces it under the profile lock
        // that every goal write takes, so the rule can change without a schema change.

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
