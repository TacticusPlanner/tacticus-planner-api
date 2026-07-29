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
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.GuildId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.TacticusUserId).IsRequired();
        builder.Property(entity => entity.TacticusUserIdHash).HasMaxLength(32);

        // HasVogenConversion() only targets the non-nullable struct overload; ProfileId here is nullable
        // (an unlinked member), so its converter is written out by hand — see GuildConfiguration for the
        // same pattern on ConfiguredByProfileId.
        builder.Property(entity => entity.ProfileId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? ProfileId.From(value.Value) : (ProfileId?)null);
        builder.Property(entity => entity.Role).HasConversion<string>().IsRequired();
        builder.Property(entity => entity.Level);
        builder.Property(entity => entity.LastActiveInGameOn);
        builder.Property(entity => entity.LastActiveInPlannerOn);
        builder.Property(entity => entity.LinkedPlayerName);
        builder.Property(entity => entity.LastSyncedAt).IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // TacticusUserId is encrypted (non-deterministically, per ADR 0005), so uniqueness is enforced on
        // its keyed hash instead — matching ciphertext can't be compared directly, same as Guild's
        // TacticusGuildId/TacticusGuildIdHash.
        builder.HasIndex(entity => new { entity.GuildId, entity.TacticusUserIdHash }).IsUnique();

        // A profile may currently belong to at most one guild's roster.
        builder
            .HasIndex(entity => entity.ProfileId)
            .IsUnique()
            .HasFilter($"{PostgresNaming.SnakeCase(nameof(GuildMember.ProfileId))} IS NOT NULL");

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
