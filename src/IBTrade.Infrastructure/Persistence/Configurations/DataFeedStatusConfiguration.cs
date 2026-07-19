using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

public class DataFeedStatusConfiguration : IEntityTypeConfiguration<DataFeedStatus>
{
    public void Configure(EntityTypeBuilder<DataFeedStatus> builder)
    {
        builder.ToTable("data_feed_statuses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Feed).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Environment).HasMaxLength(16);
        builder.Property(x => x.AccountId).HasMaxLength(32);
        builder.Property(x => x.Message).HasMaxLength(512);
        builder.HasIndex(x => new { x.Feed, x.Environment }).IsUnique();
    }
}
