using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TacticusPlanner.Api.Persistence.Users.Configurations;

public sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("profiles", "player");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.AccountId).HasColumnName("account_id").HasVogenConversion().IsRequired();
        builder.Property(entity => entity.DisplayName).HasColumnName("display_name").IsRequired();
        builder.Property(entity => entity.TacticusUserId).HasColumnName("tacticus_user_id");
        builder.Property(entity => entity.TacticusUserIdHash).HasColumnName("tacticus_user_id_hash").HasMaxLength(32);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(entity => entity.AccountId).IsUnique();
        builder
            .HasIndex(entity => entity.TacticusUserIdHash)
            .IsUnique()
            .HasFilter("tacticus_user_id_hash IS NOT NULL");
    }
}
