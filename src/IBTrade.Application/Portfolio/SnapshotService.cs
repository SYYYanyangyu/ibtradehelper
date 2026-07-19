using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Portfolio;

/// <summary>
/// 净值快照服务：把当前组合状态固化成每日一条快照，供"每天赚多少"做差分。
/// 同时记录个股每日快照，用于"每只股票当天贡献多少"的归因。
/// 仅保留真实快照能力（CaptureTodayAsync）；历史假数据生成（SeedHistory）与
/// 恒定持仓回填（RebuildHistory）已移除——历史每日净值改由 IBKR Flex 报表的逐日 NAV 段直接入库，
/// 全链路零伪造。
/// </summary>
public class SnapshotService(
    IPortfolioService portfolioService,
    IDailySnapshotRepository snapshotRepo,
    IPositionDailySnapshotRepository positionSnapshotRepo) : ISnapshotService
{
    public async Task<DateTime> CaptureTodayAsync(string? accountId, decimal? cashBalance = null, DateTime? asOf = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // 显式 asOf：允许回填历史某天。未指定时：非交易日禁止写入（勿把周末标记价写回上周五）。
        if (!asOf.HasValue && !TradingCalendar.CanMutateDailySnapshot(now))
            return DateTime.SpecifyKind(TradingCalendar.LatestTradingDay(now), DateTimeKind.Utc);

        var summary = await portfolioService.GetSummaryAsync(accountId, null, ct);
        var key = string.IsNullOrWhiteSpace(accountId) ? "ALL" : accountId;
        // 交易日写「今天」；显式 asOf 尊重调用方（历史回填）。
        var today = asOf.HasValue ? asOf.Value.Date : now.Date;
        today = DateTime.SpecifyKind(today, DateTimeKind.Utc);

        // 显式传入现金则用传入值；否则（如每日后台定时快照 CaptureTodayAsync(null)）
        // 保留该账户同日已存在的真实现金余额，避免把 IB 的融资负债（真实现金）覆盖成 0，
        // 否则净清算值会被高估（总市值 + 0 而非 总市值 + 负现金）。TWS 实时同步写入的才是真实现金。
        var prior = await snapshotRepo.GetByDateAsync(key, today, ct);
        var cash = cashBalance ?? prior?.CashBalance ?? 0m;

        await snapshotRepo.UpsertAsync(new DailySnapshot
        {
            AccountId = key,
            DateUtc = today,
            TotalMarketValue = summary.TotalMarketValue,
            CashBalance = cash,
            TotalUnrealizedPnl = summary.TotalUnrealizedPnl,
            TotalRealizedPnl = summary.TotalRealizedPnl,
            TotalPnl = summary.TotalPnl,
            PositionCount = summary.PositionCount,
            Source = DataSources.Estimate
        }, ct);

        // 记录当日个股快照（用于逐标的盈亏归因）。
        // 若当日已存在历史快照写入的常规收盘价（来自 Flex 逐日 NAV 段），则保留该常规收盘价，
        // 避免盘后/实时价覆盖后污染每日盈亏的差分（常规时段涨、盘后跌时不会误报当日亏损）。
        var existingPosSnaps = (await positionSnapshotRepo.GetByDateAsync(key, today, ct))
            .ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
        foreach (var pos in summary.Positions)
        {
            var price = pos.MarketPrice;
            if (existingPosSnaps.TryGetValue(pos.Symbol, out var ex) && ex.MarketPrice != 0m)
                price = ex.MarketPrice; // 保留常规收盘价作为每日盈亏基准
            await positionSnapshotRepo.UpsertAsync(ToSnapshot(pos, key, today, price), ct);
        }

        await snapshotRepo.SaveChangesAsync(ct);
        await positionSnapshotRepo.SaveChangesAsync(ct);
        return today;
    }

    /// <summary>由单标的盈亏明细构造个股每日快照；市价按场景（实时价 / 历史收盘价）由调用方给定。</summary>
    private static PositionDailySnapshot ToSnapshot(PositionPnlDto pos, string key, DateTime day, decimal marketPrice)
    {
        Enum.TryParse<AssetType>(pos.AssetType, out var assetType);
        return new PositionDailySnapshot
        {
            AccountId = key,
            DateUtc = day,
            Symbol = pos.Symbol,
            AssetType = assetType,
            Currency = pos.Currency,
            Quantity = pos.Quantity,
            AvgCost = pos.AvgCost,
            MarketPrice = marketPrice
        };
    }
}
