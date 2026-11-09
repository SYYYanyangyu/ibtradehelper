using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

public class NotificationHistoryRepository(AppDbContext db) : INotificationHistoryRepository
{
    public async Task<NotificationHistory?> GetByContentHashAsync(string contentHash, CancellationToken ct = default) =>
        await db.NotificationHistories.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);

    public async Task<List<NotificationHistory>> GetRecentAsync(int hours = 24, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-hours);
        return await db.NotificationHistories.AsNoTracking()
            .Where(x => x.SentAtUtc >= since)
            .OrderByDescending(x => x.SentAtUtc)
            .ToListAsync(ct);
    }

    public async Task AddAsync(NotificationHistory notification, CancellationToken ct = default)
    {
        await db.NotificationHistories.AddAsync(notification, ct);
        await db.SaveChangesAsync(ct);
    }
}
