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
        builder.Property(entity => entity.RankTargetKey).HasMaxLength(GoalValidation.MaxRankTargetKeyLength);
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

        // The in-flight slot rule, per profile: a non-Rank Active/Paused goal occupies (unit, type); a Rank
        // one occupies (unit, normalized end target), so distinct Rank milestones coexist while an exact
        // duplicate cannot. The app-level pre-check gives the friendly 409; these indexes are the
        // concurrency backstop. Completed/Archived goals are outside the filter and never collide.
        var status = PostgresNaming.SnakeCase(nameof(Goal.Status));
        var goalType = PostgresNaming.SnakeCase(nameof(Goal.GoalType));
        builder.HasIndex(entity => new { entity.ProfileId, entity.EntityType, entity.EntityId, entity.GoalType })
            .IsUnique()
            .HasFilter($"{status} IN ('Active', 'Paused') AND {goalType} <> 'Rank'")
            .HasDatabaseName(GoalSlotIndexes.Slot);
        builder.HasIndex(entity => new { entity.ProfileId, entity.EntityType, entity.EntityId, entity.RankTargetKey })
            .IsUnique()
            .HasFilter($"{status} IN ('Active', 'Paused') AND {goalType} = 'Rank'")
            .HasDatabaseName(GoalSlotIndexes.RankTarget);

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Names of the two in-flight slot indexes, so a unique violation can be recognised as a slot
/// conflict.</summary>
public static class GoalSlotIndexes
{
    public const string Slot = "ix_goals_one_in_flight_slot";
    public const string RankTarget = "ix_goals_one_in_flight_rank_target";
}
