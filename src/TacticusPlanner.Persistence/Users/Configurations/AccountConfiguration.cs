using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TacticusPlanner.Persistence.Users.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id")
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.Issuer).HasColumnName("issuer").IsRequired();
        builder.Property(entity => entity.Subject).HasColumnName("subject").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(entity => new { entity.Issuer, entity.Subject }).IsUnique();

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.Account)
            .HasForeignKey<Profile>(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
