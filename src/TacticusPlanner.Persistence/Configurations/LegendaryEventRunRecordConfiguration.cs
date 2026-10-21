using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Legendary Event run records (LRE Stage 7, design D1). Lanes, teams and the roster snapshot are read and
/// written whole and never queried inside, so they are jsonb. <c>event_id</c> has no FK: a record outlives its
/// catalog entry. The run range is asserted by the recorder, which only opens runs 1–3.
/// </summary>
public sealed class LegendaryEventRunRecordConfiguration : IEntityTypeConfiguration<LegendaryEventRunRecord>
{
    public void Configure(EntityTypeBuilder<LegendaryEventRunRecord> builder)
    {
        builder.ToTable("legendary_event_run_records");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.EventId).HasMaxLength(LegendaryEventValidation.MaxEventIdLength).IsRequired();
        builder.Property(entity => entity.Run).IsRequired();
        builder.Property(entity => entity.StartedAt).IsRequired();
        builder.Property(entity => entity.ProgressAt).IsRequired();
        builder.Property(entity => entity.CatalogVersion)
            .HasMaxLength(LegendaryEventValidation.MaxCatalogVersionLength)
            .IsRequired();
        builder.Property(entity => entity.Points).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.OwnsOne(entity => entity.Lanes, lanes =>
        {
            lanes.ToJson("lanes");
            lanes.OwnsOne(lane => lane.Alpha, track => track.OwnsMany(t => t.Encounters));
            lanes.OwnsOne(lane => lane.Beta, track => track.OwnsMany(t => t.Encounters));
            lanes.OwnsOne(lane => lane.Gamma, track => track.OwnsMany(t => t.Encounters));
        });

        builder.OwnsMany(entity => entity.Teams, teams =>
        {
            teams.ToJson("teams");
            teams.OwnsMany(team => team.RunDepths, depths =>
                depths.Property(depth => depth.Source).HasConversion<string>());
        });

        // The units reuse the synced characters / mows chunk records, mapped as PlayerDataSnapshotConfiguration
        // maps them.
        builder.OwnsOne(entity => entity.Roster, roster =>
        {
            roster.ToJson("roster");
            roster.OwnsMany(value => value.Characters, characters =>
            {
                characters.OwnsMany(unit => unit.Abilities);
                characters.OwnsMany(unit => unit.EquippedItems);
            });
            roster.OwnsMany(value => value.Mows, mows => mows.OwnsMany(unit => unit.Abilities));
        });
        builder.Navigation(entity => entity.Roster).IsRequired();

        // Serves the paged read: newest first with the id as the tiebreak (the cursor carries both).
        builder.HasIndex(entity => new { entity.ProfileId, entity.EventId, entity.StartedAt, entity.Id })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("ix_legendary_event_run_records_profile_id_event_id_started_at_id");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
