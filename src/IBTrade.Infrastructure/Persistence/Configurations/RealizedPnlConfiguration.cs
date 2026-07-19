using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

/// <summary>按标的已实现盈亏表映射。</summary>
public class RealizedPnlConfiguration : IEntityTypeConfiguration<RealizedPnl>
{
    public void Configure(EntityTypeBuilder<RealizedPnl> b)
    {
        b.ToTable("RealizedPnls");
        b.Property(x => x.AccountId).IsRequired();
        b.Property(x => x.Symbol).IsRequired();
        b.Property(x => x.AssetCategory).IsRequired().HasMaxLength(8);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(8);
        b.Property(x => x.Description).HasMaxLength(256);
        // 同一账户下按标的去重查询
        b.HasIndex(x => new { x.AccountId, x.Symbol });
    }
}
