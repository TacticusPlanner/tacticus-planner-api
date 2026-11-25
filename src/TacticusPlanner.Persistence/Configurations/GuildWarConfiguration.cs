using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.GuildWars;

namespace TacticusPlanner.Persistence.Configurations;

/// <summary>
/// Guild wars (<c>guild-war-offense</c>, design D4). One row per war; at most one current war (<c>ended_at</c> null)
/// per profile, kept by a partial unique index. The season id has no FK: a war outlives its catalog season.
/// </summary>
public sealed class GuildWarConfiguration : IEntityTypeConfiguration<GuildWar>
{
    public void Configure(EntityTypeBuilder<GuildWar> builder)
    {
        builder.ToTable("guild_wars");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id).HasVogenConversion().ValueGeneratedNever();
        builder.Property(entity => entity.ProfileId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.SeasonId).HasMaxLength(GuildWarValidation.MaxIdLength).IsRequired();
        builder.Property(entity => entity.BattlefieldLevel).IsRequired();
        builder.Property(entity => entity.StartedAt).IsRequired();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.HasIndex(entity => entity.ProfileId)
            .IsUnique()
            .HasFilter("ended_at IS NULL")
            .HasDatabaseName("ux_guild_wars_profile_id_current");

        // Serves the past-wars page: newest first with the id as the tiebreak (the cursor carries both).
        builder.HasIndex(entity => new { entity.ProfileId, entity.StartedAt, entity.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_guild_wars_profile_id_started_at_id");

        builder.HasMany(entity => entity.Attacks)
            .WithOne(attack => attack.War)
            .HasForeignKey(attack => attack.WarId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(entity => entity.Profile)
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Attacks of a guild war (design D4). The lineup and enemy details are read and written whole with their attack and
/// never queried inside, so they are jsonb. <c>profile_id</c> repeats the war's for the query filter.
/// </summary>
public sealed class GuildWarAttackConfiguration : IEntityTypeConfiguration<GuildWarAttack>
{
    public void Configure(EntityTypeBuilder<GuildWarAttack> builder)
    {
        builder.ToTable("guild_war_attacks");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id).HasVogenConversion().ValueGeneratedNever();
        builder.Property(entity => entity.WarId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.ProfileId).HasVogenConversion().IsRequired();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.AttackedAt).IsRequired();
        builder.Property(entity => entity.ZoneId).HasMaxLength(GuildWarValidation.MaxIdLength).IsRequired();
        builder.Property(entity => entity.Slot).IsRequired();
        // Stored by name ("Epic"), like a team's rarity cap, so the columns read without the enums.
        builder.Property(entity => entity.RarityCap).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Result).HasConversion<string>().HasMaxLength(16);
        builder.Property(entity => entity.UnitsLost);
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.OwnsOne(entity => entity.Lineup, lineup =>
        {
            lineup.ToJson("lineup");
            lineup.OwnsMany(value => value.Characters);
            lineup.OwnsOne(value => value.Mow);
        });
        builder.Navigation(entity => entity.Lineup).IsRequired();

        builder.OwnsOne(entity => entity.Enemy, enemy =>
        {
            enemy.ToJson("enemy");
            enemy.OwnsMany(value => value.Characters, units =>
            {
                units.Property(unit => unit.Rarity).HasConversion<string>();
                units.Property(unit => unit.State).HasConversion<string>();
            });
            enemy.OwnsOne(value => value.Mow, unit =>
            {
                unit.Property(value => value.Rarity).HasConversion<string>();
                unit.Property(value => value.State).HasConversion<string>();
            });
        });

        builder.HasIndex(entity => entity.WarId).HasDatabaseName("ix_guild_war_attacks_war_id");

        builder
            .HasOne<Domain.Profiles.Profile>()
            .WithMany()
            .HasForeignKey(entity => entity.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
