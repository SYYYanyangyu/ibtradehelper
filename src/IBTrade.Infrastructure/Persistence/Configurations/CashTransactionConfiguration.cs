using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

/// <summary>现金流表映射。</summary>
public class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> b)
    {
        b.ToTable("CashTransactions");
        b.Property(x => x.AccountId).IsRequired();
        b.Property(x => x.Currency).IsRequired().HasMaxLength(8);
        b.Property(x => x.Note).HasMaxLength(256);
        // 枚举存为文本，便于直接读库排查；EF 会按名称映射
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => new { x.AccountId, x.DateUtc });
    }
}
