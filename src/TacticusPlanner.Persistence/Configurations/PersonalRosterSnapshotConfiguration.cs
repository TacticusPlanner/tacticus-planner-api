using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.RosterSnapshots;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Personal roster snapshots (<c>personal-roster-snapshots</c>). The roster is read and written whole and never
/// queried inside, so it is jsonb, mapped exactly as <see cref="LegendaryEventRunRecordConfiguration"/> maps the
/// run-record roster. The name rules are asserted by the domain type before a save.
/// </summary>
public sealed class PersonalRosterSnapshotConfiguration : IEntityTypeConfiguration<PersonalRosterSnapshot>
{
    public void Configure(EntityTypeBuilder<PersonalRosterSnapshot> builder)
    {
        builder.ToTable("personal_roster_snapshots");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.Name).HasMaxLength(PersonalRosterSnapshotValidation.MaxNameLength).IsRequired();
        builder.Property(entity => entity.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.TakenAt).IsRequired();
        builder.Property(entity => entity.CharacterCount).IsRequired();
        builder.Property(entity => entity.MowCount).IsRequired();
        builder.Property(entity => entity.ImportKey).HasMaxLength(PersonalRosterSnapshotValidation.MaxImportKeyLength);
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

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

        // Serves the list: newest first with the id as the tiebreak.
        builder.HasIndex(entity => new { entity.ProfileId, entity.TakenAt, entity.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_personal_roster_snapshots_profile_id_taken_at_id");

        // One snapshot per import source and profile; a re-run of the import skips existing keys.
        builder.HasIndex(entity => new { entity.ProfileId, entity.ImportKey })
            .IsUnique()
            .HasFilter("import_key IS NOT NULL")
            .HasDatabaseName("ux_personal_roster_snapshots_profile_id_import_key");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
