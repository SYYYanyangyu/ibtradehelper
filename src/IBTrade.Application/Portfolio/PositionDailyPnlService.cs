using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Portfolio;

/// <summary>
/// 个股每日盈亏归因：基于个股每日快照，差分出"每只股票当天贡献多少"。
/// 当日贡献 = 今日市值 − 前一日市值（已平仓标的为 −前一日市值）；有 MTM 时优先用官方 DailyPnl。
/// </summary>
public class PositionDailyPnlService(
    IPositionDailySnapshotRepository repo,
    IPortfolioService portfolioService,
    IAccountRepository accountRepo) : IPositionDailyPnlService
{
    /// <summary>前一快照日与查询日间隔超过此值，不视为「当日平仓」（避免过期期权残留拖出 −市值）。</summary>
    private const int MaxPrevGapDays = 5;

    public async Task<PositionDailyPnlDto> GetByDateAsync(
        string? accountId,
        DateTime dateUtc,
        TradeEnv? env = null,
        CancellationToken ct = default)
    {
        var day = dateUtc.Date;
        var isToday = day == DateTime.UtcNow.Date;

        if (!string.IsNullOrWhiteSpace(accountId) && accountId != "ALL")
            return await BuildForKeyAsync(accountId, day, isToday, env, ct);

        if (env is not null)
        {
            var ids = (await accountRepo.GetAllAsync(ct))
                .Where(a => a.Environment == env)
                .Select(a => a.AccountId)
                .ToList();

            // 最近交易日且实时已空仓：直接空列表（勿用历史 MTM 零数量行刷屏）
            if (isToday || IsLatestTradingDayQuery(day))
            {
                var live = await portfolioService.GetSummaryAsync(null, env, ct);
                if (live.PositionCount == 0 && live.TotalMarketValue == 0m)
                {
                    // 仍尝试当日 MTM 非零贡献；若也没有则空
                    var mtmOnly = await BuildAggregatedAsync(ids, day, ct, mtmNonZeroOnly: true);
                    if (mtmOnly.Points.Count > 0) return mtmOnly;
                    return new PositionDailyPnlDto(day, 0m, Array.Empty<PositionDailyPnlPointDto>());
                }

                if (isToday && live.PositionCount > 0)
                {
                    var todayLive = ToSnapshotsFromLive(live, env.ToString()!, day);
                    return Diff(day, todayLive, Array.Empty<PositionDailySnapshot>(), allowStaleCloseouts: false);
                }
            }

            return await BuildAggregatedAsync(ids, day, ct, mtmNonZeroOnly: false);
        }

        // 无 env：按全部真实账户聚合（不读已污染的 ALL 键）
        var allIds = (await accountRepo.GetAllAsync(ct))
            .Select(a => a.AccountId)
            .Where(id => !string.Equals(id, "ALL", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return await BuildAggregatedAsync(allIds, day, ct, mtmNonZeroOnly: false);
    }

    private static bool IsLatestTradingDayQuery(DateTime day) =>
        day.Date == TradingCalendar.LatestTradingDay(DateTime.UtcNow).Date;

    private async Task<PositionDailyPnlDto> BuildForKeyAsync(
        string key,
        DateTime day,
        bool isToday,
        TradeEnv? env,
        CancellationToken ct)
    {
        var today = await repo.GetByDateAsync(key, day, ct);

        if (today.Count == 0 && isToday)
        {
            var accountId = key == "ALL" ? null : key;
            today = ToSnapshotsFromLive(await portfolioService.GetSummaryAsync(accountId, env, ct), key, day);
        }

        if (today.Count == 0)
            return new PositionDailyPnlDto(day, 0m, Array.Empty<PositionDailyPnlPointDto>());

        var prevDate = await repo.GetPreviousDateAsync(key, day, ct);
        IReadOnlyList<PositionDailySnapshot> prev = Array.Empty<PositionDailySnapshot>();
        if (prevDate is { Year: > 1 } && (day - prevDate.Value.Date).TotalDays <= MaxPrevGapDays)
            prev = await repo.GetByDateAsync(key, prevDate.Value, ct);

        return Diff(day, today, prev, allowStaleCloseouts: true);
    }

    private async Task<PositionDailyPnlDto> BuildAggregatedAsync(
        IReadOnlyList<string> accountIds,
        DateTime day,
        CancellationToken ct,
        bool mtmNonZeroOnly)
    {
        if (accountIds.Count == 0)
            return new PositionDailyPnlDto(day, 0m, Array.Empty<PositionDailyPnlPointDto>());

        var todayRows = new List<PositionDailySnapshot>();
        var prevRows = new List<PositionDailySnapshot>();

        foreach (var id in accountIds)
        {
            todayRows.AddRange(await repo.GetByDateAsync(id, day, ct));
            var prevDate = await repo.GetPreviousDateAsync(id, day, ct);
            if (prevDate is { Year: > 1 } && (day - prevDate.Value.Date).TotalDays <= MaxPrevGapDays)
                prevRows.AddRange(await repo.GetByDateAsync(id, prevDate.Value, ct));
        }

        if (mtmNonZeroOnly)
        {
            todayRows = todayRows.Where(r => r.DailyPnl != 0m).ToList();
            prevRows = [];
        }

        if (todayRows.Count == 0 && prevRows.Count == 0)
            return new PositionDailyPnlDto(day, 0m, Array.Empty<PositionDailyPnlPointDto>());

        return Diff(day, todayRows, prevRows, allowStaleCloseouts: !mtmNonZeroOnly);
    }

    private static PositionDailyPnlDto Diff(
        DateTime day,
        IReadOnlyList<PositionDailySnapshot> today,
        IReadOnlyList<PositionDailySnapshot> prev,
        bool allowStaleCloseouts)
    {
        static Dictionary<string, (PositionDailySnapshot Row, decimal Mv)> Map(
            IReadOnlyList<PositionDailySnapshot> rows)
        {
            return rows
                .GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var first = g.First();
                        var mv = g.Sum(x => x.MarketValue);
                        var daily = g.Sum(x => x.DailyPnl);
                        var qty = g.Sum(x => x.Quantity);
                        var merged = new PositionDailySnapshot
                        {
                            AccountId = first.AccountId,
                            DateUtc = first.DateUtc,
                            Symbol = first.Symbol,
                            AssetType = first.AssetType,
                            Currency = first.Currency,
                            Quantity = qty,
                            AvgCost = first.AvgCost,
                            MarketPrice = qty != 0 ? mv / qty : first.MarketPrice,
                            DailyPnl = daily
                        };
                        return (merged, mv);
                    },
                    StringComparer.OrdinalIgnoreCase);
        }

        var todayMap = Map(today);
        var prevMap = Map(prev);

        var hasMtm = todayMap.Values.Any(t => t.Row.DailyPnl != 0m);
        var points = new List<PositionDailyPnlPointDto>();

        foreach (var (symbol, (t, mv)) in todayMap)
        {
            var mvPrev = prevMap.TryGetValue(symbol, out var p) ? p.Mv : 0m;
            var contribution = hasMtm ? t.DailyPnl : mv - mvPrev;
            // MTM 段常含 closeQuantity=0 的噪声行（贡献与市值皆 0）——丢弃
            if (Math.Abs(contribution) < 0.01m && Math.Abs(mv) < 0.01m && Math.Abs(mvPrev) < 0.01m)
                continue;
            points.Add(new PositionDailyPnlPointDto(
                symbol,
                t.AssetType.ToString(),
                Math.Round(mv, 2),
                Math.Round(mvPrev, 2),
                Math.Round(contribution, 2),
                Math.Round(t.UnrealizedPnl, 2),
                0m));
        }

        if (allowStaleCloseouts && !hasMtm)
        {
            foreach (var (symbol, (_, mvPrev)) in prevMap)
            {
                if (todayMap.ContainsKey(symbol)) continue;
                if (Math.Abs(mvPrev) < 0.01m) continue;
                points.Add(new PositionDailyPnlPointDto(
                    symbol,
                    prevMap[symbol].Row.AssetType.ToString(),
                    0m,
                    Math.Round(mvPrev, 2),
                    Math.Round(-mvPrev, 2),
                    0m,
                    0m));
            }
        }

        var meaningful = points
            .Where(x => Math.Abs(x.DailyContribution) >= 0.01m || Math.Abs(x.MarketValue) >= 0.01m)
            .ToList();

        var denom = meaningful.Sum(x => Math.Abs(x.DailyContribution));
        var ranked = meaningful
            .Select(x => x with { Share = denom == 0 ? 0m : Math.Abs(x.DailyContribution) / denom })
            .OrderByDescending(x => x.DailyContribution)
            .ToList();

        return new PositionDailyPnlDto(day, Math.Round(ranked.Sum(x => x.DailyContribution), 2), ranked);
    }

    private static List<PositionDailySnapshot> ToSnapshotsFromLive(PortfolioSummaryDto summary, string key, DateTime day)
    {
        return summary.Positions.Select(p =>
        {
            Enum.TryParse<AssetType>(p.AssetType, out var assetType);
            return new PositionDailySnapshot
            {
                AccountId = key,
                DateUtc = day,
                Symbol = p.Symbol,
                AssetType = assetType,
                Currency = p.Currency,
                Quantity = p.Quantity,
                AvgCost = p.AvgCost,
                MarketPrice = p.MarketPrice
            };
        }).ToList();
    }
}
