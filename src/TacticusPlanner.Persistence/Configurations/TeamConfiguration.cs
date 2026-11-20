using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Teams;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Personal teams (<c>personal-teams</c>). One row per team (design D1), its lineup in <c>text[]</c> columns read and
/// written only through the team (ADR 0011). The team rules live in <see cref="TeamRules"/>, never in CHECK
/// constraints; the database keeps only keys, uniqueness and nullability.
/// </summary>
public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId)
            .HasVogenConversion()
            .IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.Name).HasMaxLength(TeamValidation.MaxNameLength).IsRequired();
        builder.Property(entity => entity.NameKey).HasMaxLength(TeamValidation.MaxNameLength).IsRequired();
        builder.Property(entity => entity.ImportKey).HasMaxLength(TeamValidation.MaxImportKeyLength);
        builder.Property(entity => entity.SortOrder).IsRequired();
        builder.PrimitiveCollection(entity => entity.Modes)
            .IsRequired()
            .ElementType(element => element.HasConversion<string>());
        builder.PrimitiveCollection(entity => entity.CharacterIds).IsRequired();
        builder.PrimitiveCollection(entity => entity.MowIds).IsRequired();
        builder.Property(entity => entity.MowFlexStartIndex);
        builder.Property(entity => entity.CampaignGroupId).HasMaxLength(TeamValidation.MaxUnitIdLength);
        builder.PrimitiveCollection(entity => entity.IncursionMowIds).IsRequired();
        builder.Property(entity => entity.Notes).HasMaxLength(TeamValidation.MaxNotesLength);
        // Stored by name ("Epic"), as the modes are, so the column reads without the enum (D13).
        builder.Property(entity => entity.RarityCap).HasConversion<string>().HasMaxLength(16);
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // Names are unique per profile ignoring case (D2); the endpoint pre-checks and returns teamNameTaken.
        // Its leading profile_id also serves the list and the foreign key.
        builder.HasIndex(entity => new { entity.ProfileId, entity.NameKey })
            .IsUnique()
            .HasDatabaseName("ux_teams_profile_id_name_key");

        // One team per V1 source team and profile; a re-run of the import skips existing keys (D8).
        builder.HasIndex(entity => new { entity.ProfileId, entity.ImportKey })
            .IsUnique()
            .HasFilter("import_key IS NOT NULL")
            .HasDatabaseName("ux_teams_profile_id_import_key");

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
