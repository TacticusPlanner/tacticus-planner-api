using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>Tables live in <c>public</c> with a <c>legendary_event_</c> prefix (design D1): the repo has
/// never created a PostgreSQL schema and this change does not introduce the first one.</summary>
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
        builder.Property(entity => entity.EventId).HasMaxLength(LegendaryEventPlanRules.MaxEventIdLength).IsRequired();
        builder.Property(entity => entity.CatalogVersion).HasMaxLength(LegendaryEventPlanRules.MaxCatalogVersionLength).IsRequired();
        builder.Property(entity => entity.Notes).HasMaxLength(LegendaryEventPlanRules.MaxNotesLength);
        builder.Property(entity => entity.ShowPaidOptions).IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // One plan per profile and event; also the backstop for two callers lazily creating the same plan
        // at once (the loser gets the 409 with the winner's plan — design D4).
        builder.HasIndex(entity => new { entity.ProfileId, entity.EventId })
            .IsUnique()
            .HasDatabaseName("ix_legendary_event_plans_profile_id_event_id");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(entity => entity.Teams)
            .WithOne(team => team.Plan)
            .HasForeignKey(team => team.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
