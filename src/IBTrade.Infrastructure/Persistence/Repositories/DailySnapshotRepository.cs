using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

/// <summary>每日净值快照仓储实现（EF Core）。</summary>
public class DailySnapshotRepository(AppDbContext db) : IDailySnapshotRepository
{
    public async Task<IReadOnlyList<DailySnapshot>> GetRecentAsync(string accountId, int days, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        return await db.DailySnapshots
            .Where(s => s.AccountId == accountId && s.DateUtc >= since)
            .OrderBy(s => s.DateUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DailySnapshot>> GetRecentAggregatedAsync(
        IReadOnlyCollection<string> accountIds, int days, CancellationToken ct = default)
    {
        if (accountIds.Count == 0) return Array.Empty<DailySnapshot>();
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rows = await db.DailySnapshots.AsNoTracking()
            .Where(s => accountIds.Contains(s.AccountId) && s.DateUtc >= since)
            .ToListAsync(ct);

        return rows
            .GroupBy(s => s.DateUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailySnapshot
            {
                AccountId = "ALL",
                DateUtc = DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                TotalMarketValue = g.Sum(x => x.TotalMarketValue),
                CashBalance = g.Sum(x => x.CashBalance),
                TotalUnrealizedPnl = g.Sum(x => x.TotalUnrealizedPnl),
                TotalRealizedPnl = g.Sum(x => x.TotalRealizedPnl),
                TotalPnl = g.Sum(x => x.TotalPnl),
                PositionCount = g.Sum(x => x.PositionCount),
                Source = g.Any(x => x.Source == "Flex") ? "Flex" : (g.First().Source ?? "Flex")
            })
            .ToList();
    }

    public async Task<DailySnapshot?> GetByDateAsync(string accountId, DateTime dateUtc, CancellationToken ct = default)
    {
        var day = dateUtc.Date;
        return await db.DailySnapshots
            .FirstOrDefaultAsync(s => s.AccountId == accountId && s.DateUtc == day, ct);
    }

    public async Task UpsertAsync(DailySnapshot snapshot, CancellationToken ct = default)
    {
        var day = snapshot.DateUtc.Date;
        var existing = await db.DailySnapshots
            .FirstOrDefaultAsync(s => s.AccountId == snapshot.AccountId && s.DateUtc == day, ct);

        if (existing is null)
        {
            snapshot.DateUtc = day;
            if (string.IsNullOrWhiteSpace(snapshot.Source))
                snapshot.Source = DataSources.Flex;
            db.DailySnapshots.Add(snapshot);
            return;
        }

        // 仅显式 Flex 官方 NAV 不可被 Gateway / 估算覆盖（空 Source 允许盘中覆盖）
        var existingFlex = string.Equals(existing.Source, DataSources.Flex, StringComparison.OrdinalIgnoreCase);
        var incomingFlex = string.Equals(snapshot.Source, DataSources.Flex, StringComparison.OrdinalIgnoreCase);
        if (existingFlex && !incomingFlex)
            return;

        existing.TotalMarketValue = snapshot.TotalMarketValue;
        existing.CashBalance = snapshot.CashBalance;
        existing.TotalUnrealizedPnl = snapshot.TotalUnrealizedPnl;
        existing.TotalRealizedPnl = snapshot.TotalRealizedPnl;
        existing.TotalPnl = snapshot.TotalPnl;
        existing.PositionCount = snapshot.PositionCount;
        existing.Source = string.IsNullOrWhiteSpace(snapshot.Source) ? existing.Source : snapshot.Source;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
