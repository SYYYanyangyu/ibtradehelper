using IBTrade.Application.MarketData;
using IBTrade.Application.MarketData.Dtos;
using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Portfolio;

public interface IPerformanceCompareService
{
    Task<PerformanceCompareDto> GetAsync(
        string? accountId, string range, TradeEnv? env, CancellationToken ct = default);
}

public class PerformanceCompareService(
    IDailyPnlService dailyPnl,
    IndexHistoryCache indexCache) : IPerformanceCompareService
{
    /// <summary>低于此净值视为空仓残留，不能当区间起点（否则 1 月会从 $4 涨到几千变成几万倍）。</summary>
    private const decimal MeaningfulNav = 50m;

    private static readonly (string Id, string Label, string? FredId, IndexHistoryCache.IndexContract[] Attempts)[] Indices =
    [
        ("hsi", "恒生", null, []),
        ("spx", "标普500", "SP500", [
            new("SPX", "CBOE", "USD")
        ]),
        ("dji", "道琼斯", "DJIA", [
            new("INDU", "CME", "USD")
        ]),
        ("ixic", "纳斯达克", "NASDAQCOM", [
            new("COMP", "NASDAQ", "USD")
        ])
    ];

    public async Task<PerformanceCompareDto> GetAsync(
        string? accountId, string range, TradeEnv? env, CancellationToken ct = default)
    {
        var key = NormalizeRange(range);
        var days = DaysFor(key);
        var pnl = await dailyPnl.GetAsync(accountId, days, env, ct);
        var calendarStart = WindowStart(key, pnl);

        var indexRows = await Task.WhenAll(Indices.Select(async spec =>
        {
            var (bars, err) = await indexCache.GetAsync(spec.Id, spec.Attempts, spec.FredId, DurationFor(key), ct);
            return (spec.Id, spec.Label, bars, err);
        }));

        var port = BuildPortfolio(pnl, calendarStart);
        var align = port.From ?? calendarStart;

        var empty = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var idxMaps = indexRows.ToDictionary(
            r => r.Id,
            r => RebaseIndex(ToCloseMap(r.bars, align), align),
            StringComparer.Ordinal);

        var dates = (port.Pct.Keys)
            .Concat(idxMaps.Values.SelectMany(m => m.Keys))
            .Distinct(StringComparer.Ordinal)
            .Where(d => d.CompareTo(align) >= 0)
            .OrderBy(d => d)
            .ToList();

        var points = dates.Select(d => new PerformancePointDto(
            d,
            Pick(port.Pct, d),
            Pick(idxMaps.GetValueOrDefault("hsi") ?? empty, d),
            Pick(idxMaps.GetValueOrDefault("spx") ?? empty, d),
            Pick(idxMaps.GetValueOrDefault("dji") ?? empty, d),
            Pick(idxMaps.GetValueOrDefault("ixic") ?? empty, d))).ToList();

        var series = new List<PerformanceSeriesDto>
        {
            new("portfolio", "我的组合", port.ReturnPct, port.Pct.Count > 0 ? "ok" : "empty",
                port.Pct.Count > 0 ? "Flex 官方净值 · 时间加权" : "没有 Flex 净值，先同步报表")
        };
        foreach (var row in indexRows)
        {
            var map = row.Id switch
            {
                "hsi" => idxMaps.GetValueOrDefault("hsi") ?? empty,
                "spx" => idxMaps.GetValueOrDefault("spx") ?? empty,
                "dji" => idxMaps.GetValueOrDefault("dji") ?? empty,
                _ => idxMaps.GetValueOrDefault("ixic") ?? empty
            };
            var src = row.Id == "hsi"
                ? "IB Gateway"
                : "美联储 FRED 官方日线 · 与组合同日起算";
            series.Add(new(
                row.Id,
                row.Label,
                Last(map),
                row.err is null && map.Count > 0 ? "ok" : "error",
                row.err ?? src));
        }

        return new PerformanceCompareDto(
            key,
            DateTime.UtcNow.ToString("o"),
            "我的组合",
            series,
            points,
            port.From,
            port.To,
            port.StartNav,
            port.EndNav,
            port.Method);
    }

    private readonly record struct PortfolioCurve(
        Dictionary<string, decimal> Pct,
        decimal? ReturnPct,
        string? From,
        string? To,
        decimal? StartNav,
        decimal? EndNav,
        string Method);

    private static PortfolioCurve BuildPortfolio(IReadOnlyList<DailyPnlPointDto> pnl, string calendarStart)
    {
        var none = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var rows = pnl
            .Where(p => p.Date.CompareTo(calendarStart) >= 0 && p.NetLiquidation > 0)
            .OrderBy(p => p.Date)
            .ToList();
        var first = rows.FindIndex(p => p.NetLiquidation >= MeaningfulNav);
        if (first < 0)
            return new(none, null, null, null, null, null, "区间内没有可用净值");

        rows = rows.Skip(first).ToList();
        var startNav = rows[0].NetLiquidation;
        var prev = startNav;
        decimal growth = 1m;
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal) { [rows[0].Date] = 0m };

        for (var i = 1; i < rows.Count; i++)
        {
            var nav = rows[i].NetLiquidation;
            var cf = rows[i].ExternalCashFlow;
            if (prev > 0)
            {
                var r = (nav - prev - cf) / prev;
                growth *= 1m + r;
            }
            map[rows[i].Date] = decimal.Round((growth - 1m) * 100m, 4);
            prev = nav;
        }

        var endNav = rows[^1].NetLiquidation;
        var method = rows.Any(p => p.ExternalCashFlow != 0)
            ? "时间加权（已剔除入金/出金）· 与组合同日起算指数"
            : "期末净值 / 期初净值 − 1（Flex 无出入金记录，未剔除转账）";
        return new(
            map,
            decimal.Round((growth - 1m) * 100m, 4),
            rows[0].Date,
            rows[^1].Date,
            decimal.Round(startNav, 2),
            decimal.Round(endNav, 2),
            method);
    }

    private static Dictionary<string, decimal> ToCloseMap(IReadOnlyList<CandleDto> bars, string start) =>
        bars
            .Select(b => (Date: b.Time.ToString("yyyy-MM-dd"), b.Close))
            .Where(x => x.Date.CompareTo(start) >= 0 && x.Close > 0)
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.Last().Close, StringComparer.Ordinal);

    private static Dictionary<string, decimal> RebaseIndex(IReadOnlyDictionary<string, decimal> raw, string start)
    {
        decimal? first = null;
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var d in raw.Keys.OrderBy(k => k))
        {
            if (d.CompareTo(start) < 0) continue;
            var px = raw[d];
            if (px <= 0) continue;
            first ??= px;
            map[d] = decimal.Round((px / first.Value - 1m) * 100m, 4);
        }
        return map;
    }

    private static decimal? Last(IReadOnlyDictionary<string, decimal> map) =>
        map.Count == 0 ? null : map[map.Keys.OrderBy(k => k).Last()];

    private static decimal? Pick(IReadOnlyDictionary<string, decimal> map, string date) =>
        map.TryGetValue(date, out var v) ? v : null;

    private static string NormalizeRange(string? range) =>
        (range ?? "1Y").Trim().ToUpperInvariant() switch
        {
            "1M" or "1MO" or "MONTH" => "1M",
            "3M" => "3M",
            "6M" => "6M",
            "YTD" => "YTD",
            "ALL" => "ALL",
            _ => "1Y"
        };

    private static int DaysFor(string range) => range switch
    {
        "1M" => 45,
        "3M" => 110,
        "6M" => 200,
        "YTD" => 400,
        "ALL" => 4000,
        _ => 400
    };

    private static string DurationFor(string range) => range switch
    {
        "1M" => "2 M",
        "3M" => "6 M",
        "6M" => "1 Y",
        "ALL" => "5 Y",
        _ => "1 Y"
    };

    private static string WindowStart(string range, IReadOnlyList<DailyPnlPointDto> pnl)
    {
        var today = DateTime.UtcNow.Date;
        var start = range switch
        {
            "1M" => today.AddMonths(-1),
            "3M" => today.AddMonths(-3),
            "6M" => today.AddMonths(-6),
            "YTD" => new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "ALL" => pnl.Count > 0 && DateTime.TryParse(pnl[0].Date, out var first) ? first.Date : today.AddYears(-5),
            _ => today.AddYears(-1)
        };
        return start.ToString("yyyy-MM-dd");
    }
}
