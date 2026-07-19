using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

public class RealizedPnlRepository(AppDbContext db) : IRealizedPnlRepository
{
    public async Task<IReadOnlyList<RealizedPnl>> GetAllAsync(CancellationToken ct = default) =>
        await db.RealizedPnls.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<RealizedPnl>> GetByAccountAsync(string accountId, CancellationToken ct = default) =>
        await db.RealizedPnls.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.Realized)
            .ToListAsync(ct);

    public async Task ReplaceByAccountsAsync(IEnumerable<string> accountIds, IEnumerable<RealizedPnl> rows, CancellationToken ct = default)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var toRemove = await db.RealizedPnls
            .Where(x => ids.Contains(x.AccountId))
            .ToListAsync(ct);
        db.RealizedPnls.RemoveRange(toRemove);
        await db.RealizedPnls.AddRangeAsync(rows, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
