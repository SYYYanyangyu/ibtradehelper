using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(32);
        builder.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.LimitPrice).HasPrecision(18, 6);
        builder.Property(x => x.SecType).HasMaxLength(8);
        builder.Property(x => x.Expiry).HasMaxLength(8);
        builder.Property(x => x.Strike).HasPrecision(18, 6);
        builder.Property(x => x.Right).HasMaxLength(1);
        builder.Property(x => x.Multiplier).HasPrecision(18, 6);
        builder.Property(x => x.FilledQuantity).HasPrecision(18, 6);
        builder.Property(x => x.AvgFillPrice).HasPrecision(18, 6);
        builder.Property(x => x.Strategy).HasMaxLength(64);
        builder.Property(x => x.Note).HasMaxLength(256);
        builder.Property(x => x.LastError).HasMaxLength(512);
        builder.Property(x => x.Side).HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(x => x.BrokerOrderId);
        builder.HasIndex(x => new { x.Symbol, x.CreatedAt });
        builder.HasIndex(x => x.Status);
    }
}
