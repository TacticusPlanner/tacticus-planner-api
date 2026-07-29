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
            .HasVogenConversion()
            .ValueGeneratedNever();

        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        // OwnsMany, not ComplexCollection: EF Core 10's ComplexCollection(...).ToJson() compiles but 500s
        // at query time against this stack (confirmed empirically on GoalConfiguration.Events — see its
        // comment). Same reason for the two collections below.
        builder.OwnsMany(entity => entity.BattleResultOverrides, chunk => chunk.ToJson("battle_result_overrides"));
        builder.OwnsMany(entity => entity.CampaignEventProgressOverrides, chunk =>
            chunk.ToJson("campaign_event_progress_overrides"));
        builder.OwnsMany(entity => entity.OnslaughtProgressOverrides, chunk => chunk.ToJson("onslaught_progress_overrides"));

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.PlayerDataOverride)
            .HasForeignKey<PlayerDataOverride>(entity => entity.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
