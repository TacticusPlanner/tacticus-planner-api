using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.RosterSnapshots;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Whole-roster snapshots (LRE Stage 7, design D4). The units reuse the synced <c>characters</c> / <c>mows</c>
/// chunk records, mapped to jsonb the same way <see cref="PlayerDataSnapshotConfiguration"/> maps them.
/// </summary>
public sealed class RosterSnapshotConfiguration : IEntityTypeConfiguration<RosterSnapshot>
{
    public void Configure(EntityTypeBuilder<RosterSnapshot> builder)
    {
        var source = PostgresNaming.SnakeCase(nameof(RosterSnapshot.Source));
        var name = PostgresNaming.SnakeCase(nameof(RosterSnapshot.Name));
        builder.ToTable("roster_snapshots", table =>
        {
            table.HasCheckConstraint(
                "ck_roster_snapshots_source",
                $"{source} IN ('{RosterSnapshotValidation.LegendaryEventRunSource}')");
            table.HasCheckConstraint(
                "ck_roster_snapshots_name",
                $"{name} IS NULL OR char_length({name}) <= {RosterSnapshotValidation.MaxNameLength}");
        });
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.Source)
            .HasConversion(
                value => SourceName(value),
                value => ParseSource(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(RosterSnapshotValidation.MaxNameLength);
        builder.Property(entity => entity.TakenAt).IsRequired();
        builder.Property(entity => entity.CatalogVersion)
            .HasMaxLength(LegendaryEventValidation.MaxCatalogVersionLength)
            .IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.OwnsMany(entity => entity.Characters, chunk =>
        {
            chunk.ToJson("characters");
            chunk.OwnsMany(unit => unit.Abilities);
            chunk.OwnsMany(unit => unit.EquippedItems);
        });

        builder.OwnsMany(entity => entity.Mows, chunk =>
        {
            chunk.ToJson("mows");
            chunk.OwnsMany(unit => unit.Abilities);
        });

        builder.HasIndex(entity => new { entity.ProfileId, entity.TakenAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_roster_snapshots_profile_id_taken_at");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static string SourceName(RosterSnapshotSource source) => source switch
    {
        RosterSnapshotSource.LegendaryEventRun => RosterSnapshotValidation.LegendaryEventRunSource,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    private static RosterSnapshotSource ParseSource(string value) => value switch
    {
        RosterSnapshotValidation.LegendaryEventRunSource => RosterSnapshotSource.LegendaryEventRun,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
