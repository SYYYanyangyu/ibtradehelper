using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

/// <summary>个股每日快照表映射。</summary>
public class PositionDailySnapshotConfiguration : IEntityTypeConfiguration<PositionDailySnapshot>
{
    public void Configure(EntityTypeBuilder<PositionDailySnapshot> builder)
    {
        builder.ToTable("PositionDailySnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AccountId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.AvgCost).HasPrecision(18, 6);
        builder.Property(x => x.MarketPrice).HasPrecision(18, 6);

        // 计算属性不入库
        builder.Ignore(x => x.MarketValue);
        builder.Ignore(x => x.UnrealizedPnl);

        // 同一账户同日同标的唯一
        builder.HasIndex(x => new { x.AccountId, x.DateUtc, x.Symbol }).IsUnique();
    }
}
