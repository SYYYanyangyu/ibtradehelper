using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

/// <summary>
/// 持仓表映射配置：字段类型、长度、索引等集中在此，保持实体纯净。
/// </summary>
public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AccountId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.AvgCost).HasPrecision(18, 6);
        builder.Property(x => x.MarketPrice).HasPrecision(18, 6);
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired();
        builder.Property(x => x.PriceSource).HasMaxLength(16).IsRequired();

        // 计算属性不入库
        builder.Ignore(x => x.MarketValue);
        builder.Ignore(x => x.UnrealizedPnl);

        builder.HasIndex(x => new { x.AccountId, x.Symbol });
    }
}
