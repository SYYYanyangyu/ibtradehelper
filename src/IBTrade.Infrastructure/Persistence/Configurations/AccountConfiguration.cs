using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AccountId).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.AccountId).IsUnique();   // 账户号唯一
        builder.Property(x => x.Broker).HasMaxLength(64);
        builder.Property(x => x.BaseCurrency).HasMaxLength(8);
        builder.Property(x => x.Alias).HasMaxLength(128);
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(8).IsRequired();
    }
}
