using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Legendary Event plans (LRE Stages 2–3) as one row: the plan is only ever read and written whole, so its teams
/// (with members, objectives and run depths), run inputs and annotations are jsonb columns. Their shape
/// invariants live in <see cref="LegendaryEventValidation.AssertInvariants"/>, checked before every save.
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

        builder.OwnsMany(entity => entity.Teams, teams =>
        {
            teams.ToJson("teams");
            teams.OwnsMany(team => team.RunDepths, depths =>
                depths.Property(depth => depth.Source).HasConversion<string>());
        });
        builder.OwnsMany(entity => entity.Runs, runs => runs.ToJson("runs"));
        builder.OwnsMany(entity => entity.Annotations, annotations =>
        {
            annotations.ToJson("annotations");
            // Stored as its served name.
            annotations.Property(entity => entity.Status)
                .HasConversion(
                    value => value == LegendaryEventAnnotationStatus.StopHere
                        ? LegendaryEventValidation.StopHereStatus
                        : LegendaryEventValidation.MaybeClearStatus,
                    value => value == LegendaryEventValidation.StopHereStatus
                        ? LegendaryEventAnnotationStatus.StopHere
                        : LegendaryEventAnnotationStatus.MaybeClear);
        });

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
