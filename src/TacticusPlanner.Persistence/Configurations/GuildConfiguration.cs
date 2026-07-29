using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class GuildConfiguration : IEntityTypeConfiguration<Guild>
{
    public void Configure(EntityTypeBuilder<Guild> builder)
    {
        builder.ToTable("guilds");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.TacticusGuildId).IsRequired();
        builder.Property(entity => entity.TacticusGuildIdHash)
            .HasMaxLength(32);
        builder.Property(entity => entity.Tag).IsRequired();
        builder.Property(entity => entity.Name).IsRequired();
        builder.Property(entity => entity.Level);
        builder.Property(entity => entity.GuildApiToken);

        // HasVogenConversion() only targets the non-nullable struct overload; ConfiguredByProfileId is a
        // nullable Vogen id, so its converter is written out by hand.
        builder.Property(entity => entity.ConfiguredByProfileId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? ProfileId.From(value.Value) : (ProfileId?)null);
        builder.Property(entity => entity.LastSyncAttemptedAt);
        builder.Property(entity => entity.LastSyncSucceededAt);
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder
            .HasIndex(entity => entity.TacticusGuildIdHash)
            .IsUnique()
            .HasFilter("tacticus_guild_id_hash IS NOT NULL");
        builder.HasIndex(entity => entity.Tag).IsUnique();

        // Nullable — a profile purge must not take the guild registration down with it.
        builder
            .HasOne(entity => entity.ConfiguredByProfile)
            .WithMany()
            .HasForeignKey(entity => entity.ConfiguredByProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
