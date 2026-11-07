using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

public class PushSubscriptionRepository(AppDbContext db) : IPushSubscriptionRepository
{
    public async Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken ct = default) =>
        await db.PushSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Endpoint == endpoint, ct);

    public async Task<List<PushSubscription>> GetActiveSubscriptionsAsync(CancellationToken ct = default) =>
        await db.PushSubscriptions.AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

    public async Task<List<PushSubscription>> GetScheduledBriefSubscriptionsAsync(CancellationToken ct = default) =>
        await db.PushSubscriptions.AsNoTracking()
            .Where(x => x.IsActive && x.EnableScheduledBriefs)
            .ToListAsync(ct);

    public async Task<List<PushSubscription>> GetBreakingNewsSubscriptionsAsync(CancellationToken ct = default) =>
        await db.PushSubscriptions.AsNoTracking()
            .Where(x => x.IsActive && x.EnableBreakingNews)
            .ToListAsync(ct);

    public async Task AddAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        await db.PushSubscriptions.AddAsync(subscription, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        db.PushSubscriptions.Update(subscription);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string endpoint, CancellationToken ct = default)
    {
        await db.PushSubscriptions.Where(x => x.Endpoint == endpoint).ExecuteDeleteAsync(ct);
    }

    public async Task<int> CountActiveSubscriptionsAsync(CancellationToken ct = default) =>
        await db.PushSubscriptions.CountAsync(x => x.IsActive, ct);
}
