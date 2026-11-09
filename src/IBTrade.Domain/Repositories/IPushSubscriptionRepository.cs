using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

public interface IPushSubscriptionRepository
{
    Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken ct = default);
    Task<List<PushSubscription>> GetActiveSubscriptionsAsync(CancellationToken ct = default);
    Task<List<PushSubscription>> GetScheduledBriefSubscriptionsAsync(CancellationToken ct = default);
    Task<List<PushSubscription>> GetBreakingNewsSubscriptionsAsync(CancellationToken ct = default);
    Task AddAsync(PushSubscription subscription, CancellationToken ct = default);
    Task UpdateAsync(PushSubscription subscription, CancellationToken ct = default);
    Task DeleteAsync(string endpoint, CancellationToken ct = default);
    Task<int> CountActiveSubscriptionsAsync(CancellationToken ct = default);
}
