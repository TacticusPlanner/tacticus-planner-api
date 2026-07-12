using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Persistence.Users.Guilds;

namespace TacticusPlanner.Persistence.Users.Configurations;

public sealed class GuildConfiguration : IEntityTypeConfiguration<Guild>
{
    public void Configure(EntityTypeBuilder<Guild> builder)
    {
        builder.ToTable("guilds");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.TacticusGuildId).HasColumnName("tacticus_guild_id").IsRequired();
        builder.Property(entity => entity.Tag).HasColumnName("tag").IsRequired();
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.Level).HasColumnName("level");
        builder.Property(entity => entity.GuildApiToken).HasColumnName("guild_api_token");

        // HasVogenConversion() only targets the non-nullable struct overload; ConfiguredByProfileId is a
        // nullable Vogen id, so its converter is written out by hand.
        builder.Property(entity => entity.ConfiguredByProfileId)
            .HasColumnName("configured_by_profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? ProfileId.From(value.Value) : (ProfileId?)null);
        builder.Property(entity => entity.LastSyncAttemptedAt).HasColumnName("last_sync_attempted_at");
        builder.Property(entity => entity.LastSyncSucceededAt).HasColumnName("last_sync_succeeded_at");
        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(entity => entity.TacticusGuildId).IsUnique();
        builder.HasIndex(entity => entity.Tag).IsUnique();

        // Nullable — a profile purge must not take the guild registration down with it.
        builder
            .HasOne(entity => entity.ConfiguredByProfile)
            .WithMany()
            .HasForeignKey(entity => entity.ConfiguredByProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
