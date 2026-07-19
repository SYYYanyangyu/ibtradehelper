using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("trades");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AccountId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.Price).HasPrecision(18, 6);
        builder.Property(x => x.Commission).HasPrecision(18, 6);

        builder.Ignore(x => x.GrossAmount);   // 计算属性不入库
        builder.Ignore(x => x.NetAmount);

        builder.HasIndex(x => new { x.AccountId, x.TradeTimeUtc });
    }
}
