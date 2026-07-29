using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasVogenConversion()
            .ValueGeneratedNever();
        builder.Property(entity => entity.Issuer).IsRequired();
        builder.Property(entity => entity.Subject).IsRequired();
        builder.Property(entity => entity.LastSeenAt);
        builder.Property(entity => entity.CreatedAt).IsRequired();
        builder.Property(entity => entity.UpdatedAt).IsRequired();

        builder.HasIndex(entity => new { entity.Issuer, entity.Subject }).IsUnique();

        builder
            .HasOne(entity => entity.Profile)
            .WithOne(entity => entity.Account)
            .HasForeignKey<Profile>(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
