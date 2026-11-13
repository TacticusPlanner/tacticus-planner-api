using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.EventShops;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Event shop plans as one row: the plan is only ever read and written whole, so its cart entries are one jsonb
/// column. Their shape invariants live in <see cref="EventShopValidation.AssertInvariants"/>, checked before every save.
/// </summary>
public sealed class EventShopPlanConfiguration : IEntityTypeConfiguration<EventShopPlan>
{
    public void Configure(EntityTypeBuilder<EventShopPlan> builder)
    {
        builder.ToTable("event_shop_plans");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.EventId).HasMaxLength(EventShopValidation.MaxEventIdLength).IsRequired();
        builder.Property(entity => entity.CatalogVersion)
            .HasMaxLength(EventShopValidation.MaxCatalogVersionLength)
            .IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.OwnsMany(entity => entity.Entries, entries => entries.ToJson("entries"));

        // One plan per (profile, event); also the backstop when two callers create the same plan at once.
        builder.HasIndex(entity => new { entity.ProfileId, entity.EventId })
            .IsUnique()
            .HasDatabaseName("ix_event_shop_plans_profile_id_event_id");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
