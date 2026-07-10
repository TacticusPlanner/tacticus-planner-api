using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TacticusPlanner.Persistence.Users.PlayerData;

namespace TacticusPlanner.Persistence.Users.Configurations;

public sealed class PlayerDataSnapshotConfiguration : IEntityTypeConfiguration<PlayerDataSnapshot>
{
    public void Configure(EntityTypeBuilder<PlayerDataSnapshot> builder)
    {
        builder.ToTable("player_data_snapshots");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("profile_id")
            .HasVogenConversion()
            .ValueGeneratedNever();

        builder.Property(entity => entity.ConfigHash).HasColumnName("config_hash").IsRequired();
        builder.Property(entity => entity.TacticusLastUpdatedOn).HasColumnName("tacticus_last_updated_on");
        builder.Property(entity => entity.SourceHash).HasColumnName("source_hash").IsRequired();
        builder.Property(entity => entity.SchemaVersion).HasColumnName("schema_version");
        builder.Property(entity => entity.SyncedAt).HasColumnName("synced_at").IsRequired();
        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // A flat string dictionary, not an owned object graph — mapped via converter rather than
        // OwnsOne/ToJson, which is reserved for the structured chunk payloads below.
        var chunkHashesComparer = new ValueComparer<Dictionary<string, string>>(
            (left, right) => (left ?? new()).OrderBy(pair => pair.Key)
                .SequenceEqual((right ?? new()).OrderBy(pair => pair.Key)),
            value => value
                .Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
            value => new Dictionary<string, string>(value));

        builder.Property(entity => entity.ChunkHashes)
            .HasColumnName("chunk_hashes")
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<Dictionary<string, string>, string>(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    value => JsonSerializer.Deserialize<Dictionary<string, string>>(value, (JsonSerializerOptions?)null)
                        ?? new Dictionary<string, string>()))
            .Metadata.SetValueComparer(chunkHashesComparer);

        // Each chunk below is its own jsonb column via EF Core's JSON owned-entity mapping
        // (OwnsOne/OwnsMany + ToJson()) rather than a hand-serialized string, per ADR 0002/0007.

        builder.OwnsOne(entity => entity.PlayerDetails, chunk => chunk.ToJson("player_details"));

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

        builder.OwnsMany(entity => entity.InventoryUpgrades, chunk => chunk.ToJson("inventory_upgrades"));
        builder.OwnsMany(entity => entity.InventoryItems, chunk => chunk.ToJson("inventory_items"));
        builder.OwnsMany(entity => entity.InventoryShards, chunk => chunk.ToJson("inventory_shards"));

        builder.OwnsOne(entity => entity.Inventory, chunk =>
        {
            chunk.ToJson("inventory");
            chunk.OwnsMany(inventory => inventory.XpBooks);
            chunk.OwnsOne(inventory => inventory.AbilityBadges, badges =>
            {
                badges.OwnsMany(b => b.Imperial);
                badges.OwnsMany(b => b.Xenos);
                badges.OwnsMany(b => b.Chaos);
            });
            chunk.OwnsOne(inventory => inventory.Components, components =>
            {
                components.OwnsOne(c => c.Imperial);
                components.OwnsOne(c => c.Xenos);
                components.OwnsOne(c => c.Chaos);
            });
            chunk.OwnsMany(inventory => inventory.ForgeBadges);
            chunk.OwnsOne(inventory => inventory.Orbs, orbs =>
            {
                orbs.OwnsMany(o => o.Imperial);
                orbs.OwnsMany(o => o.Xenos);
                orbs.OwnsMany(o => o.Chaos);
            });
        });

        builder.OwnsMany(entity => entity.CampaignProgress, chunk => chunk.ToJson("campaign_progress"));
        builder.OwnsMany(entity => entity.CampaignEventsProgress, chunk => chunk.ToJson("campaign_events_progress"));

        builder.OwnsOne(entity => entity.LiveProgress, chunk =>
        {
            chunk.ToJson("live_progress");
            chunk.OwnsMany(live => live.BattleAttempts);
            chunk.OwnsOne(live => live.GameModeTokens, tokens =>
            {
                tokens.OwnsOne(t => t.Arena);
                tokens.OwnsOne(t => t.GuildRaid, guildRaid =>
                {
                    guildRaid.OwnsOne(g => g.Tokens);
                    guildRaid.OwnsOne(g => g.BombTokens);
                });
                tokens.OwnsOne(t => t.Onslaught);
                tokens.OwnsOne(t => t.SalvageRun);
            });
        });

        builder.OwnsMany(entity => entity.LreProgress, chunk =>
        {
            chunk.ToJson("lre_progress");
            chunk.OwnsOne(lre => lre.Alpha, track => track.OwnsMany(t => t.Encounters));
            chunk.OwnsOne(lre => lre.Beta, track => track.OwnsMany(t => t.Encounters));
            chunk.OwnsOne(lre => lre.Gamma, track => track.OwnsMany(t => t.Encounters));
            chunk.OwnsOne(lre => lre.CurrentEventTokens);
        });

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.PlayerDataSnapshot)
            .HasForeignKey<PlayerDataSnapshot>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
