using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IBTrade.Infrastructure.Persistence.Repositories;

/// <summary>
/// 持仓仓储实现：领域接口在此落地为具体的 EF Core 查询。
/// </summary>
public class PositionRepository(AppDbContext db) : IPositionRepository
{
    public async Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default) =>
        await db.Positions.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Position>> GetByAccountAsync(string accountId, CancellationToken ct = default) =>
        await db.Positions.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .ToListAsync(ct);

    public async Task AddAsync(Position position, CancellationToken ct = default) =>
        await db.Positions.AddAsync(position, ct);

    public async Task ReplaceByAccountAsync(string accountId, IEnumerable<Position> positions, CancellationToken ct = default)
    {
        var existing = db.Positions.Where(p => p.AccountId == accountId);
        db.Positions.RemoveRange(existing);
        await db.Positions.AddRangeAsync(positions, ct);
    }

    public async Task<string> MergeGatewayMarksAsync(string accountId, IEnumerable<Position> gatewayPositions, CancellationToken ct = default)
    {
        var existing = await db.Positions.Where(p => p.AccountId == accountId).ToListAsync(ct);
        var bySymbol = existing
            .GroupBy(p => PositionKey(p.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var gwList = gatewayPositions.ToList();
        var gwSymbols = new HashSet<string>(gwList.Select(p => PositionKey(p.Symbol)), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var priceUpdated = 0;
        var structureUpdated = 0;
        var added = 0;
        var removed = 0;

        foreach (var gp in gwList)
        {
            if (bySymbol.TryGetValue(PositionKey(gp.Symbol), out var ex))
            {
                // 空 Source 或 Flex：数量/均价以 Flex 为准，Gateway 只刷市价
                var flexOwned = string.IsNullOrWhiteSpace(ex.Source)
                                || string.Equals(ex.Source, DataSources.Flex, StringComparison.OrdinalIgnoreCase);
                if (flexOwned)
                {
                    if (string.IsNullOrWhiteSpace(ex.Source))
                        ex.Source = DataSources.Flex;
                    ex.MarketPrice = gp.MarketPrice;
                    if (ex.Multiplier <= 1m && gp.Multiplier > 1m)
                        ex.Multiplier = gp.Multiplier;
                    ex.PriceSource = DataSources.Gateway;
                    ex.UpdatedAtUtc = now;
                    priceUpdated++;
                }
                else
                {
                    ex.Quantity = gp.Quantity;
                    ex.AvgCost = gp.AvgCost;
                    ex.MarketPrice = gp.MarketPrice;
                    ex.Multiplier = gp.Multiplier > 0 ? gp.Multiplier : ex.Multiplier;
                    ex.Currency = gp.Currency;
                    ex.AssetType = gp.AssetType;
                    ex.Source = DataSources.Gateway;
                    ex.PriceSource = DataSources.Gateway;
                    ex.UpdatedAtUtc = now;
                    structureUpdated++;
                }
            }
            else
            {
                gp.AccountId = accountId;
                gp.Source = DataSources.Gateway;
                gp.PriceSource = DataSources.Gateway;
                gp.UpdatedAtUtc = now;
                await db.Positions.AddAsync(gp, ct);
                added++;
            }
        }

        foreach (var ex in existing.Where(e =>
                     string.Equals(e.Source, DataSources.Gateway, StringComparison.OrdinalIgnoreCase)
                     && !gwSymbols.Contains(PositionKey(e.Symbol))))
        {
            db.Positions.Remove(ex);
            removed++;
        }

        var flexKept = existing.Count(e =>
            (string.IsNullOrWhiteSpace(e.Source) || string.Equals(e.Source, DataSources.Flex, StringComparison.OrdinalIgnoreCase))
            && !gwSymbols.Contains(PositionKey(e.Symbol)));

        return $"priceRefresh={priceUpdated}, gatewayUpdate={structureUpdated}, added={added}, removed={removed}, flexKeptAbsentFromGw={flexKept}";
    }

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await db.SaveChangesAsync(ct);

    private static string PositionKey(string symbol) =>
        string.Concat((symbol ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
