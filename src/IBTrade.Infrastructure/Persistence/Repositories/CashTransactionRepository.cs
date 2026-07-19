using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

/// <summary>现金流仓储实现（EF Core）。</summary>
public class CashTransactionRepository(AppDbContext db) : ICashTransactionRepository
{
    public async Task<IReadOnlyList<CashTransaction>> GetSinceAsync(string accountId, DateTime fromUtc, CancellationToken ct = default) =>
        await db.CashTransactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.DateUtc >= fromUtc)
            .OrderBy(x => x.DateUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CashTransaction>> GetByAccountAsync(string accountId, CancellationToken ct = default) =>
        await db.CashTransactions.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.DateUtc)
            .ToListAsync(ct);

    public async Task AddAsync(CashTransaction txn, CancellationToken ct = default) =>
        await db.CashTransactions.AddAsync(txn, ct);

    public async Task AddRangeAsync(IEnumerable<CashTransaction> txns, CancellationToken ct = default) =>
        await db.CashTransactions.AddRangeAsync(txns, ct);

    public Task DeleteRangeAsync(IEnumerable<CashTransaction> txns, CancellationToken ct = default)
    {
        db.CashTransactions.RemoveRange(txns);
        return Task.CompletedTask;
    }

    public async Task ReplaceByAccountAsync(string accountId, IEnumerable<CashTransaction> txns, CancellationToken ct = default)
    {
        var existing = await db.CashTransactions
            .Where(x => x.AccountId == accountId)
            .ToListAsync(ct);
        db.CashTransactions.RemoveRange(existing);
        await db.CashTransactions.AddRangeAsync(txns, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
