using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

public class TradeRepository(AppDbContext db) : ITradeRepository
{
    public async Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) =>
        await db.Trades.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Trade>> GetByAccountAsync(string accountId, CancellationToken ct = default) =>
        await db.Trades.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.TradeTimeUtc)
            .ToListAsync(ct);

    public async Task AddRangeAsync(IEnumerable<Trade> trades, CancellationToken ct = default) =>
        await db.Trades.AddRangeAsync(trades, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await db.SaveChangesAsync(ct);
}
