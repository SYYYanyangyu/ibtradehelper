using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

public class NotificationHistoryConfiguration : IEntityTypeConfiguration<NotificationHistory>
{
    public void Configure(EntityTypeBuilder<NotificationHistory> builder)
    {
        builder.ToTable("notification_histories");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Type).HasColumnName("type").IsRequired().HasMaxLength(64);
        builder.Property(x => x.Title).HasColumnName("title").IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).HasColumnName("body").IsRequired().HasMaxLength(1024);
        builder.Property(x => x.Url).HasColumnName("url").HasMaxLength(512);
        builder.Property(x => x.Tag).HasColumnName("tag").HasMaxLength(64);

        builder.Property(x => x.SentAtUtc).HasColumnName("sent_at_utc");
        builder.Property(x => x.SubscriptionsSent).HasColumnName("subscriptions_sent");
        builder.Property(x => x.SuccessCount).HasColumnName("success_count");
        builder.Property(x => x.FailureCount).HasColumnName("failure_count");
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => x.SentAtUtc);
        builder.HasIndex(x => x.ContentHash);
        builder.HasIndex(x => new { x.Type, x.SentAtUtc });
    }
}
