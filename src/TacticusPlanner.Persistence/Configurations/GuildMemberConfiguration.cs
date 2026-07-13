using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class GuildMemberConfiguration : IEntityTypeConfiguration<GuildMember>
{
    public void Configure(EntityTypeBuilder<GuildMember> builder)
    {
        builder.ToTable("guild_members");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.GuildId).HasColumnName("guild_id").HasVogenConversion().IsRequired();
        builder.Property(entity => entity.TacticusUserId).HasColumnName("tacticus_user_id").IsRequired();
        builder.Property(entity => entity.TacticusUserIdHash).HasColumnName("tacticus_user_id_hash").HasMaxLength(32);

        // HasVogenConversion() only targets the non-nullable struct overload; ProfileId here is nullable
        // (an unlinked member), so its converter is written out by hand — see GuildConfiguration for the
        // same pattern on ConfiguredByProfileId.
        builder.Property(entity => entity.ProfileId)
            .HasColumnName("profile_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? ProfileId.From(value.Value) : (ProfileId?)null);
        builder.Property(entity => entity.Role).HasColumnName("role").HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Level).HasColumnName("level");
        builder.Property(entity => entity.LastActiveInGameOn).HasColumnName("last_active_in_game_on");
        builder.Property(entity => entity.LastActiveInPlannerOn).HasColumnName("last_active_in_planner_on");
        builder.Property(entity => entity.LinkedPlayerName).HasColumnName("linked_player_name");
        builder.Property(entity => entity.LastSyncedAt).HasColumnName("last_synced_at").IsRequired();
        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // TacticusUserId is encrypted (non-deterministically, per ADR 0005), so uniqueness is enforced on
        // its keyed hash instead — matching ciphertext can't be compared directly, same as Guild's
        // TacticusGuildId/TacticusGuildIdHash.
        builder.HasIndex(entity => new { entity.GuildId, entity.TacticusUserIdHash }).IsUnique();

        // A profile may currently belong to at most one guild's roster.
        builder
            .HasIndex(entity => entity.ProfileId)
            .IsUnique()
            .HasFilter("profile_id IS NOT NULL");

        builder
            .HasOne(entity => entity.Guild)
            .WithMany(entity => entity.Members)
            .HasForeignKey(entity => entity.GuildId)
            .OnDelete(DeleteBehavior.Cascade);

        // Nullable and set-null on delete: a profile purge must not delete the synchronized member row,
        // only sever the link (see Guild Phase 1 spec's failure/consistency rules).
        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
