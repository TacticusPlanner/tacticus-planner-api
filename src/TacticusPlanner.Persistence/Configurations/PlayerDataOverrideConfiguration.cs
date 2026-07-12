using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.PlayerData;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class PlayerDataOverrideConfiguration : IEntityTypeConfiguration<PlayerDataOverride>
{
    public void Configure(EntityTypeBuilder<PlayerDataOverride> builder)
    {
        builder.ToTable("player_data_overrides");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("profile_id")
            .HasVogenConversion()
            .ValueGeneratedNever();

        builder.Property(entity => entity.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.OwnsMany(entity => entity.BattleResultOverrides, chunk => chunk.ToJson("battle_result_overrides"));
        builder.OwnsMany(entity => entity.CampaignProgressOverrides, chunk => chunk.ToJson("campaign_progress_overrides"));

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.PlayerDataOverride)
            .HasForeignKey<PlayerDataOverride>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
