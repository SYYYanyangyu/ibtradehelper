using System.Globalization;
using IBTrade.Application.Discipline;
using IBTrade.Application.MarketData;
using IBTrade.Application.MarketData.Dtos;
using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Portfolio;

public interface IWeeklyReviewService
{
    Task<WeeklyReviewDto> GetAsync(string? accountId, TradeEnv? env, int weeks, CancellationToken ct = default);
}

/// <summary>
/// 每周复盘：以 FIFO 平仓批次（按开仓成交聚合）为一笔交易，按最后平仓的美东周分组；
/// 期权开仓逐条对照纪律设置（白名单、时段、权利金、前日 VIX、FOMC、当日连亏）以及「7 天内到期隔夜持有」。
/// 账户周收益来自 Flex 净值；基准为 QQQ 日线（Gateway）。拿不到的数据给 null，不估算。
/// </summary>
public class WeeklyReviewService(
    ITradePnlService tradePnl,
    IDailyPnlService dailyPnl,
    IHistoricalDataService hist,
    MarketContextProvider market,
    IDisciplineGate gate) : IWeeklyReviewService
{
    private const string Benchmark = "QQQ";
    /// <summary>期初净值低于此值时百分比没有意义（刚入金的账户会出现上万 %）。</summary>
    private const decimal MinStartNav = 100m;
    private static readonly TimeZoneInfo Ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["symbol"] = "非白名单标的",
        ["window"] = "时段外开仓",
        ["premium"] = "权利金超限",
        ["overnight"] = "短期期权隔夜",
        ["streak"] = "连亏后继续开仓",
        ["vix"] = "高 VIX 日开仓",
        ["fomc"] = "FOMC 日开仓"
    };

    private sealed class Position
    {
        public long OpenTradeId;
        public string Symbol = "";
        public bool IsOption;
        public bool IsLong;
        public DateTime OpenedUtc;
        public DateTime ClosedUtc;
        public decimal Premium;
        public decimal Net;
        public DateOnly? Expiry;
        public List<string> Violations = [];
    }

    public async Task<WeeklyReviewDto> GetAsync(string? accountId, TradeEnv? env, int weeks, CancellationToken ct = default)
    {
        weeks = Math.Clamp(weeks, 1, 26);
        var s = gate.Settings;
        var notes = new List<string>();
        var nowEt = ToEt(DateTime.UtcNow);
        var thisMonday = Monday(DateOnly.FromDateTime(nowEt));
        var firstMonday = thisMonday.AddDays(-7 * (weeks - 1));

        var pnl = await tradePnl.GetAsync(accountId, env, ct);
        var positions = pnl.Closed
            .GroupBy(c => (c.AccountId, c.OpenTradeId))
            .Select(g =>
            {
                var first = g.First();
                var occ = ParseOcc(first.Symbol);
                return new Position
                {
                    OpenTradeId = first.OpenTradeId,
                    Symbol = first.Symbol,
                    IsOption = first.AssetType == "Option" || occ is not null,
                    IsLong = first.Direction != "Short",
                    OpenedUtc = first.OpenedAtUtc,
                    ClosedUtc = g.Max(c => c.ClosedAtUtc),
                    Premium = g.Sum(c => c.OpenPrice * c.Quantity * c.Multiplier),
                    Net = g.Sum(c => c.NetPnl),
                    Expiry = occ?.Expiry
                };
            })
            .OrderBy(p => p.ClosedUtc)
            .ToList();

        var (vix, vixErr) = await market.GetVixAsync(ct);
        var (fomc, fomcErr) = await market.GetFomcAsync(ct);
        if (vixErr is not null) notes.Add($"VIX 未取到，未判定高 VIX：{vixErr}");
        if (fomcErr is not null) notes.Add($"美联储日程未取到，未判定 FOMC：{fomcErr}");
        var fomcSet = fomc.ToHashSet();
        var start = TimeSpan.Parse(s.EntryStart, CultureInfo.InvariantCulture);
        var end = TimeSpan.Parse(s.EntryEnd, CultureInfo.InvariantCulture);

        foreach (var p in positions.Where(p => p.IsOption && p.IsLong))
        {
            var openEt = ToEt(p.OpenedUtc);
            var openDay = DateOnly.FromDateTime(openEt);
            var root = ParseOcc(p.Symbol)?.Root ?? p.Symbol.Trim();
            if (s.Symbols.Count > 0 && !s.Symbols.Contains(root.ToUpperInvariant())) p.Violations.Add("symbol");
            if (openEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                || openEt.TimeOfDay < start || openEt.TimeOfDay >= end) p.Violations.Add("window");
            if (s.MaxPremium > 0 && p.Premium > s.MaxPremium) p.Violations.Add("premium");
            if (p.Expiry is { } exp && exp.DayNumber - openDay.DayNumber <= 7
                && DateOnly.FromDateTime(ToEt(p.ClosedUtc)) > openDay) p.Violations.Add("overnight");
            if (s.MaxPrevVix > 0 && vix.Count > 0
                && MarketContextProvider.PrevVix(vix, openDay) is { } pv && pv.Close >= s.MaxPrevVix) p.Violations.Add("vix");
            if (s.SkipFomcDays && fomcSet.Contains(openDay)) p.Violations.Add("fomc");
            if (s.StopAfterLosses > 0 && LossStreakBefore(positions, p.OpenedUtc, openDay) >= s.StopAfterLosses)
                p.Violations.Add("streak");
        }

        var daily = await SafeDailyAsync(accountId, env, weeks * 7 + 10, notes, ct);
        var bench = await SafeBenchmarkAsync(weeks, notes, ct);

        var result = new List<ReviewWeekDto>();
        for (var w = thisMonday; w >= firstMonday; w = w.AddDays(-7))
        {
            var wEnd = w.AddDays(6);
            var inWeek = positions.Where(p =>
            {
                var d = DateOnly.FromDateTime(ToEt(p.ClosedUtc));
                return d >= w && d <= wEnd;
            }).ToList();
            result.Add(BuildWeek(w, wEnd, inWeek, daily, bench));
        }

        var period = positions.Where(p => DateOnly.FromDateTime(ToEt(p.ClosedUtc)) >= firstMonday).ToList();
        return new WeeklyReviewDto(
            DateTime.UtcNow,
            Benchmark,
            $"白名单 {string.Join("/", s.Symbols)} · 美东 {s.EntryStart}–{s.EntryEnd} · 权利金 ≤ ${s.MaxPremium:0} · "
            + $"前日 VIX < {s.MaxPrevVix} · {(s.SkipFomcDays ? "避开 FOMC · " : "")}连亏 {s.StopAfterLosses} 笔停手 · 7 天内到期不过夜",
            notes,
            Tally(period),
            result);
    }

    private static ReviewWeekDto BuildWeek(DateOnly w, DateOnly wEnd, List<Position> ps,
        IReadOnlyList<DailyPnlPointDto>? daily, IReadOnlyList<CandleDto>? bench)
    {
        var wins = ps.Where(p => p.Net > 0).ToList();
        var losses = ps.Where(p => p.Net < 0).ToList();
        var grossWin = wins.Sum(p => p.Net);
        var grossLoss = -losses.Sum(p => p.Net);
        decimal peak = 0, cum = 0, dd = 0;
        foreach (var p in ps)
        {
            cum += p.Net;
            peak = Math.Max(peak, cum);
            dd = Math.Max(dd, peak - cum);
        }
        var clean = ps.Where(p => p.Violations.Count == 0).ToList();
        var dirty = ps.Where(p => p.Violations.Count > 0).ToList();

        decimal? acctPnl = null, acctPct = null;
        if (daily is { Count: > 0 })
        {
            var pts = daily.Where(d => InRange(d.Date, w, wEnd)).ToList();
            if (pts.Count > 0)
            {
                acctPnl = pts.Sum(d => d.TradingPnl);
                var before = daily.LastOrDefault(d => string.CompareOrdinal(d.Date, w.ToString("yyyy-MM-dd")) < 0);
                var startNav = before?.NetLiquidation ?? pts[0].NetLiquidation - pts[0].DailyPnl;
                if (startNav >= MinStartNav) acctPct = Math.Round(acctPnl.Value / startNav * 100m, 2);
            }
        }

        decimal? benchPct = null;
        if (bench is { Count: > 0 })
        {
            var prev = bench.LastOrDefault(b => DateOnly.FromDateTime(b.Time) < w);
            var last = bench.LastOrDefault(b => DateOnly.FromDateTime(b.Time) <= wEnd && DateOnly.FromDateTime(b.Time) >= w);
            if (prev is { Close: > 0 } && last is not null)
                benchPct = Math.Round((last.Close / prev.Close - 1m) * 100m, 2);
        }

        return new ReviewWeekDto(
            w.ToString("yyyy-MM-dd"),
            wEnd.ToString("yyyy-MM-dd"),
            ps.Count,
            wins.Count,
            Rate(wins.Count, ps.Count),
            Math.Round(ps.Sum(p => p.Net), 2),
            wins.Count > 0 ? Math.Round(grossWin / wins.Count, 2) : 0,
            losses.Count > 0 ? Math.Round(-grossLoss / losses.Count, 2) : 0,
            grossLoss > 0 ? Math.Round(grossWin / grossLoss, 2) : null,
            ps.Count > 0 ? Math.Round(ps.Sum(p => p.Net) / ps.Count, 2) : 0,
            Math.Round(dd, 2),
            clean.Count,
            Math.Round(clean.Sum(p => p.Net), 2),
            Rate(clean.Count(p => p.Net > 0), clean.Count),
            dirty.Count,
            Math.Round(dirty.Sum(p => p.Net), 2),
            acctPnl is null ? null : Math.Round(acctPnl.Value, 2),
            acctPct,
            benchPct,
            Tally(ps),
            dirty.OrderBy(p => p.Net).Take(5)
                .Select(p => new ReviewTradeDto(p.OpenTradeId, p.Symbol, p.OpenedUtc, p.ClosedUtc,
                    Math.Round(p.Premium, 2), Math.Round(p.Net, 2), p.Violations.Select(v => Labels[v]).ToList()))
                .ToList());
    }

    private static List<ReviewViolationDto> Tally(List<Position> ps) =>
        Labels.Select(kv =>
            {
                var hit = ps.Where(p => p.Violations.Contains(kv.Key)).ToList();
                return new ReviewViolationDto(kv.Key, kv.Value, hit.Count, hit.Count(p => p.Net > 0),
                    Math.Round(hit.Sum(p => p.Net), 2));
            })
            .ToList();

    /// <summary>开仓当刻之前、同一美东日内已平仓交易的末尾连亏笔数。</summary>
    private static int LossStreakBefore(List<Position> all, DateTime openUtc, DateOnly day)
    {
        var streak = 0;
        foreach (var p in all)
        {
            if (p.ClosedUtc >= openUtc) break;
            if (DateOnly.FromDateTime(ToEt(p.ClosedUtc)) != day) continue;
            streak = p.Net <= 0 ? streak + 1 : 0;
        }
        return streak;
    }

    private async Task<IReadOnlyList<DailyPnlPointDto>?> SafeDailyAsync(string? accountId, TradeEnv? env, int days,
        List<string> notes, CancellationToken ct)
    {
        try
        {
            var d = await dailyPnl.GetAsync(accountId, Math.Min(days, 365), env, ct);
            if (d.Count == 0) notes.Add("没有净值快照，账户周收益留空");
            return d;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notes.Add($"净值读取失败：{ex.GetBaseException().Message}");
            return null;
        }
    }

    private async Task<IReadOnlyList<CandleDto>?> SafeBenchmarkAsync(int weeks, List<string> notes, CancellationToken ct)
    {
        try
        {
            var bars = await hist.GetBarsAsync(Benchmark, null, "USD", $"{weeks * 7 + 14} D", "1 day", ct);
            if (bars.Count == 0) notes.Add($"{Benchmark} 日线为空，基准留空");
            return bars;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notes.Add($"{Benchmark} 日线取不到（Gateway）：{ex.GetBaseException().Message}");
            return null;
        }
    }

    private static (string Root, DateOnly Expiry)? ParseOcc(string symbol)
    {
        var i = symbol.LastIndexOf(' ');
        if (i < 0) return null;
        var tail = symbol[(i + 1)..];
        if (tail.Length != 15 || tail[6] is not ('C' or 'P')) return null;
        if (!DateOnly.TryParseExact(tail[..6], "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
            return null;
        return (symbol[..i].Trim(), exp);
    }

    private static bool InRange(string date, DateOnly from, DateOnly to) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        && d >= from && d <= to;

    private static decimal Rate(int part, int total) => total > 0 ? Math.Round(part * 100m / total, 1) : 0;

    private static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private static DateTime ToEt(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ny);
}
