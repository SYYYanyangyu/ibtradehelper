using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

public interface INotificationHistoryRepository
{
    Task<NotificationHistory?> GetByContentHashAsync(string contentHash, CancellationToken ct = default);
    Task<List<NotificationHistory>> GetRecentAsync(int hours = 24, CancellationToken ct = default);
    Task AddAsync(NotificationHistory notification, CancellationToken ct = default);
}
