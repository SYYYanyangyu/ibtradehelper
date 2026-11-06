using IBTrade.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IBTrade.Infrastructure.Persistence.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Endpoint).HasColumnName("endpoint").IsRequired().HasMaxLength(512);
        builder.HasIndex(x => x.Endpoint).IsUnique();

        builder.Property(x => x.P256dh).HasColumnName("p256dh").IsRequired().HasMaxLength(128);
        builder.Property(x => x.Auth).HasColumnName("auth").IsRequired().HasMaxLength(64);
        builder.Property(x => x.OwnershipToken).HasColumnName("ownership_token").IsRequired().HasMaxLength(256);

        builder.Property(x => x.EnableScheduledBriefs).HasColumnName("enable_scheduled_briefs");
        builder.Property(x => x.EnableBreakingNews).HasColumnName("enable_breaking_news");
        builder.Property(x => x.ScheduledBriefTimes).HasColumnName("scheduled_brief_times").HasMaxLength(128);
        builder.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(64);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.LastNotifiedUtc).HasColumnName("last_notified_utc");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");

        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.FailureCount).HasColumnName("failure_count");
        builder.Property(x => x.LastErrorMessage).HasColumnName("last_error_message").HasMaxLength(512);

        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => new { x.IsActive, x.EnableScheduledBriefs });
        builder.HasIndex(x => new { x.IsActive, x.EnableBreakingNews });
    }
}
