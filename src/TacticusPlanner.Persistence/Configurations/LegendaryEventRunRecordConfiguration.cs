using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.RosterSnapshots;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Legendary Event run records (LRE Stage 7, design D1). Lanes and teams are read and written whole and never
/// queried inside, so they are jsonb. <c>event_id</c> has no FK: a record outlives its catalog entry. Each record
/// owns one roster snapshot (unique FK, set null if the snapshot is ever removed).
/// </summary>
public sealed class LegendaryEventRunRecordConfiguration : IEntityTypeConfiguration<LegendaryEventRunRecord>
{
    public void Configure(EntityTypeBuilder<LegendaryEventRunRecord> builder)
    {
        var run = PostgresNaming.SnakeCase(nameof(LegendaryEventRunRecord.Run));
        builder.ToTable("legendary_event_run_records", table => table.HasCheckConstraint(
            "ck_legendary_event_run_records_run",
            $"{run} BETWEEN {LegendaryEventValidation.MinRun} AND {LegendaryEventValidation.MaxRun}"));
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

        // HasVogenConversion() only targets the non-nullable struct overload (see GuildConfiguration).
        builder.Property(entity => entity.RosterSnapshotId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? RosterSnapshotId.From(value.Value) : (RosterSnapshotId?)null);

        builder.OwnsOne(entity => entity.Lanes, lanes =>
        {
            lanes.ToJson("lanes");
            lanes.OwnsOne(lane => lane.Alpha, track => track.OwnsMany(t => t.Encounters));
            lanes.OwnsOne(lane => lane.Beta, track => track.OwnsMany(t => t.Encounters));
            lanes.OwnsOne(lane => lane.Gamma, track => track.OwnsMany(t => t.Encounters));
        });

        builder.OwnsMany(entity => entity.Teams, teams => teams.ToJson("teams"));

        builder.HasIndex(entity => new { entity.ProfileId, entity.EventId, entity.StartedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_legendary_event_run_records_profile_id_event_id_started_at");

        builder.HasIndex(entity => entity.RosterSnapshotId)
            .IsUnique()
            .HasDatabaseName("ix_legendary_event_run_records_roster_snapshot_id");

        builder
            .HasOne(entity => entity.RosterSnapshot)
            .WithOne()
            .HasForeignKey<LegendaryEventRunRecord>(entity => entity.RosterSnapshotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
