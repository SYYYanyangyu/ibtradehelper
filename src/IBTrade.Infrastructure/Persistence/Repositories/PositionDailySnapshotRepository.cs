using IBTrade.Domain.Entities;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

/// <summary>个股每日快照仓储实现（EF Core）。</summary>
public class PositionDailySnapshotRepository(AppDbContext db) : IPositionDailySnapshotRepository
{
    public async Task<IReadOnlyList<PositionDailySnapshot>> GetByDateAsync(string accountId, DateTime dateUtc, CancellationToken ct = default)
    {
        var day = dateUtc.Date;
        return await db.PositionDailySnapshots
            .Where(s => s.AccountId == accountId && s.DateUtc == day)
            .OrderBy(s => s.Symbol)
            .ToListAsync(ct);
    }

    public async Task<DateTime?> GetPreviousDateAsync(string accountId, DateTime dateUtc, CancellationToken ct = default)
    {
        var day = dateUtc.Date;
        return await db.PositionDailySnapshots
            .Where(s => s.AccountId == accountId && s.DateUtc < day)
            .Select(s => s.DateUtc)
            .Distinct()
            .OrderByDescending(d => d)
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(PositionDailySnapshot snapshot, CancellationToken ct = default)
    {
        var day = snapshot.DateUtc.Date;
        snapshot.DateUtc = day;

        // 先查本地未提交的 Added（同批 Flex 可能重复同一 AccountId+Date+Symbol，或 ALL 多账户叠加）
        var existing = db.PositionDailySnapshots.Local
            .FirstOrDefault(s =>
                s.AccountId == snapshot.AccountId
                && s.DateUtc == day
                && string.Equals(s.Symbol, snapshot.Symbol, StringComparison.OrdinalIgnoreCase));

        existing ??= await db.PositionDailySnapshots
            .FirstOrDefaultAsync(s =>
                s.AccountId == snapshot.AccountId
                && s.DateUtc == day
                && s.Symbol == snapshot.Symbol, ct);

        if (existing is null)
        {
            db.PositionDailySnapshots.Add(snapshot);
            return;
        }

        existing.AssetType = snapshot.AssetType;
        existing.Currency = snapshot.Currency;
        existing.Quantity = snapshot.Quantity;
        existing.AvgCost = snapshot.AvgCost;
        existing.MarketPrice = snapshot.MarketPrice;
        // DailyPnl：Flex MTM 写入后必须能覆盖；CaptureToday 默认 0 不得抹掉已有官方 MTM。
        if (snapshot.DailyPnl != 0m)
            existing.DailyPnl = snapshot.DailyPnl;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
