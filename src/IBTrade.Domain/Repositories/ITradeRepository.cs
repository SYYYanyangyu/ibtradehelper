using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

public interface ITradeRepository
{
    Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Trade>> GetByAccountAsync(string accountId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Trade> trades, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
