using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Portfolio;

/// <summary>
/// 每日盈亏服务：基于每日净值快照差分出"每天赚多少"。
/// </summary>
public class DailyPnlService(
    IDailySnapshotRepository snapshotRepo,
    ICashTransactionRepository cashTxnRepo,
    IAccountRepository accountRepo)
    : IDailyPnlService
{
    public async Task<IReadOnlyList<DailyPnlPointDto>> GetAsync(
        string? accountId,
        int days = 30,
        TradeEnv? env = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<Domain.Entities.DailySnapshot> snaps;

        if (!string.IsNullOrWhiteSpace(accountId) && accountId != "ALL")
        {
            snaps = await snapshotRepo.GetRecentAsync(accountId, days, ct);
        }
        else
        {
            // 禁止读已污染的 AccountId=ALL 行（单环境 Flex 摄入会覆盖）。
            // 无 env：按全部真实账户聚合；有 env：仅该环境。
            var accounts = await accountRepo.GetAllAsync(ct);
            var ids = accounts
                .Where(a => env is null || a.Environment == env)
                .Select(a => a.AccountId)
                .Where(id => !string.Equals(id, "ALL", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            snaps = await snapshotRepo.GetRecentAggregatedAsync(ids, days, ct);
        }

        snaps = snaps
            .Where(s => s.DateUtc.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .OrderBy(s => s.DateUtc)
            .ToList();

        // 丢弃明显污染的 NAV（仅现金为负、无持仓市值，与前日相差过大）
        if (snaps.Count > 1)
        {
            var cleaned = new List<Domain.Entities.DailySnapshot> { snaps[0] };
            for (var i = 1; i < snaps.Count; i++)
            {
                var s = snaps[i];
                var prev = cleaned[^1];
                var nav = s.NetLiquidation;
                var prevNav = prev.NetLiquidation;
                if (s.TotalMarketValue == 0m && nav < 0m && prevNav > 10_000m)
                    continue;
                if (prevNav > 10_000m && nav < prevNav * 0.05m)
                    continue;
                cleaned.Add(s);
            }
            snaps = cleaned;
        }

        if (snaps.Count == 0)
            return Array.Empty<DailyPnlPointDto>();

        var since = snaps[0].DateUtc;
        // 现金流：单账户用该账户；环境汇总时暂不拆扣（避免 ALL 混用），按 0 处理更安全
        var flowByDay = new Dictionary<DateTime, decimal>();
        if (!string.IsNullOrWhiteSpace(accountId) && accountId != "ALL")
        {
            var cashTxns = await cashTxnRepo.GetSinceAsync(accountId, since, ct);
            flowByDay = cashTxns
                .GroupBy(t => t.DateUtc.Date)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        }

        var firstNet = snaps[0].NetLiquidation;
        var cumTrading = 0m;
        var prevNet = snaps[0].NetLiquidation;
        var result = new List<DailyPnlPointDto>(snaps.Count);

        for (var i = 0; i < snaps.Count; i++)
        {
            var s = snaps[i];
            var net = s.NetLiquidation;
            var daily = i == 0 ? 0m : net - prevNet;
            var external = i > 0 && flowByDay.TryGetValue(s.DateUtc.Date, out var f) ? f : 0m;
            var trading = daily - external;
            if (i > 0) cumTrading += trading;

            var prevMv = i > 0 ? snaps[i - 1].TotalMarketValue : s.TotalMarketValue;
            var pct = prevMv != 0 ? Math.Round(daily / prevMv * 100m, 2) : 0m;
            var cum = net - firstNet;

            result.Add(new DailyPnlPointDto(
                s.DateUtc.ToString("yyyy-MM-dd"),
                Math.Round(s.NetLiquidation, 2),
                Math.Round(daily, 2),
                pct,
                Math.Round(cum, 2),
                Math.Round(external, 2),
                Math.Round(trading, 2),
                Math.Round(cumTrading, 2)));

            prevNet = net;
        }

        return result;
    }
}
