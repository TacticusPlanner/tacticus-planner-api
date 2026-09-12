using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class GuildRaidSyncStateConfiguration : IEntityTypeConfiguration<GuildRaidSyncState>
{
    public void Configure(EntityTypeBuilder<GuildRaidSyncState> builder)
    {
        builder.ToTable("guild_raid_sync_states");
        builder.HasKey(entity => entity.GuildId);
        builder.Property(entity => entity.GuildId).HasVogenConversion().ValueGeneratedNever();
        builder.Property(entity => entity.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ObservedAt);
        builder.Property(entity => entity.LastAttemptedAt).IsRequired();
        builder.Property(entity => entity.ActiveSeasonId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? GuildRaidSeasonId.From(value.Value) : (GuildRaidSeasonId?)null);
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();
        builder.HasOne(entity => entity.Guild).WithOne()
            .HasForeignKey<GuildRaidSyncState>(entity => entity.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(entity => entity.ActiveSeason).WithMany()
            .HasForeignKey(entity => entity.ActiveSeasonId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class GuildRaidSeasonConfiguration : IEntityTypeConfiguration<GuildRaidSeason>
{
    public void Configure(EntityTypeBuilder<GuildRaidSeason> builder)
    {
        builder.ToTable("guild_raid_seasons");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasVogenConversion().ValueGeneratedNever();
        builder.Property(entity => entity.GuildId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.SeasonNumber).IsRequired();
        builder.Property(entity => entity.SeasonConfigId).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.ObservedAt).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();
        builder.HasIndex(entity => new { entity.GuildId, entity.SeasonNumber }).IsUnique();
        builder.HasOne(entity => entity.Guild).WithMany()
            .HasForeignKey(entity => entity.GuildId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GuildRaidAttackConfiguration : IEntityTypeConfiguration<GuildRaidAttack>
{
    public void Configure(EntityTypeBuilder<GuildRaidAttack> builder)
    {
        builder.ToTable("guild_raid_attacks");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasVogenConversion().ValueGeneratedNever();
        builder.Property(entity => entity.GuildRaidSeasonId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.TacticusUserIdHash)
            .HasConversion(hash => hash.Value, value => TacticusUserIdHash.From(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.EncounterType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.UnitSetId).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Difficulty).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.DamageType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.CompletedAt).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();
        builder.HasIndex(entity => new { entity.GuildRaidSeasonId, entity.ContentHash }).IsUnique();
        builder.HasIndex(entity => new
        {
            entity.GuildRaidSeasonId,
            entity.TacticusUserIdHash,
            entity.CompletedAt,
        });
        builder.HasOne(entity => entity.Season).WithMany(entity => entity.Attacks)
            .HasForeignKey(entity => entity.GuildRaidSeasonId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GuildRaidAttackUnitConfiguration : IEntityTypeConfiguration<GuildRaidAttackUnit>
{
    public void Configure(EntityTypeBuilder<GuildRaidAttackUnit> builder)
    {
        builder.ToTable("guild_raid_attack_units");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasVogenConversion().ValueGeneratedNever();
        builder.Property(entity => entity.GuildRaidAttackId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.UnitId).HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();
        builder.HasIndex(entity => new { entity.GuildRaidAttackId, entity.Kind, entity.Position }).IsUnique();
        builder.HasOne(entity => entity.Attack).WithMany(entity => entity.Units)
            .HasForeignKey(entity => entity.GuildRaidAttackId).OnDelete(DeleteBehavior.Cascade);
    }
}
