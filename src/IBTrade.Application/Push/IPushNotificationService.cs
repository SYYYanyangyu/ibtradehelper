using IBTrade.Application.Push.Dtos;

namespace IBTrade.Application.Push;

public interface IPushNotificationService
{
    Task<string> GetVapidPublicKeyAsync(CancellationToken ct = default);
    Task SubscribeAsync(SubscribeRequest request, CancellationToken ct = default);
    Task UnsubscribeAsync(string endpoint, CancellationToken ct = default);
    Task<NotificationPreferences> GetPreferencesAsync(string endpoint, CancellationToken ct = default);
    Task UpdatePreferencesAsync(string endpoint, NotificationPreferences preferences, CancellationToken ct = default);
    Task SendNotificationAsync(SendPushRequest request, CancellationToken ct = default);
    Task SendScheduledBriefAsync(CancellationToken ct = default);
    Task SendScheduledBriefWithContentAsync(string briefSummary, CancellationToken ct = default);
    Task SendBreakingNewsAsync(string title, string body, string? url = null, CancellationToken ct = default);
    Task<int> GetActiveSubscriptionCountAsync(CancellationToken ct = default);
}
