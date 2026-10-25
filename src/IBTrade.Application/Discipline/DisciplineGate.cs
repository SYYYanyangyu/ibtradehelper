using System.Globalization;
using System.Text.Json;
using IBTrade.Application.MarketData;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Application.Quant.TrendScalper;
using IBTrade.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.Discipline;

public interface IDisciplineGate
{
    DisciplineSettings Settings { get; }
    void UpdateSettings(DisciplineSettings settings);
    /// <summary>手动单的纪律检查；不适用（平仓 / 正股 / 已关闭）时 Applies=false 且放行。</summary>
    Task<DisciplineCheckResult> CheckAsync(PlaceOrderRequest req, CancellationToken ct = default);
    Task<DisciplineStatus> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>
/// 下单前的硬规则：标的白名单、美东时段、前日 VIX、FOMC 日、权利金上限、顺势（开盘区间 + VWAP）、
/// 当日连亏、当日 / 本周亏损上限。行情来自 Gateway 与官方数据源，当日盈亏来自本系统的成交订单。
/// 任何一项数据取不到都按不通过处理。
/// </summary>
public class DisciplineGate : IDisciplineGate
{
    private static readonly TimeZoneInfo Ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private readonly IOrderStore _store;
    private readonly IHistoricalDataService _hist;
    private readonly MarketContextProvider _market;
    private readonly ILogger<DisciplineGate>? _logger;
    private readonly object _gate = new();
    private readonly string _path = ResolvePath();
    private DisciplineSettings _settings = new();

    public DisciplineGate(IOrderStore store, IHistoricalDataService hist, MarketContextProvider market,
        ILogger<DisciplineGate>? logger = null)
    {
        _store = store;
        _hist = hist;
        _market = market;
        _logger = logger;
        Load();
    }

    public DisciplineSettings Settings { get { lock (_gate) return Clone(_settings); } }

    public void UpdateSettings(DisciplineSettings s)
    {
        if (s.Mode is not ("Block" or "Warn")) throw new ArgumentException("Mode 只能是 Block 或 Warn");
        if (s.MaxPremium < 0) throw new ArgumentException("权利金上限不能为负");
        if (s.StopAfterLosses < 0 || s.DailyLossLimit < 0 || s.WeeklyLossLimit < 0 || s.MaxPrevVix < 0)
            throw new ArgumentException("上限不能为负");
        var start = ParseHm(s.EntryStart, "开仓开始");
        var end = ParseHm(s.EntryEnd, "开仓结束");
        if (end <= start) throw new ArgumentException("开仓结束须晚于开始");
        s.Symbols = s.Symbols.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        lock (_gate) _settings = Clone(s);
        Save();
    }

    public async Task<DisciplineStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var nowEt = NowEt();
        var ledger = BuildLedger(nowEt);
        return new DisciplineStatus
        {
            Settings = Settings,
            TodayEt = nowEt.ToString("yyyy-MM-dd"),
            TodayRealized = ledger.TodayRealized,
            TodayTrades = ledger.Today.Count,
            ConsecutiveLosses = ledger.ConsecutiveLosses,
            WeekRealized = ledger.WeekRealized,
            Context = await _market.ForDayAsync(DateOnly.FromDateTime(nowEt), ct),
            Today = ledger.Today
        };
    }

    public async Task<DisciplineCheckResult> CheckAsync(PlaceOrderRequest req, CancellationToken ct = default)
    {
        var s = Settings;
        var result = new DisciplineCheckResult { Mode = s.Mode };
        var isOpt = string.Equals(req.SecType, "OPT", StringComparison.OrdinalIgnoreCase);
        if (!s.Enabled || !isOpt || req.Side != OrderSide.Buy)
        {
            result.Summary = !s.Enabled ? "纪律闸门已关闭" : "只检查期权开仓；平仓与正股不拦";
            return result;
        }
        result.Applies = true;

        var nowEt = NowEt();
        var items = result.Items;
        var root = req.Symbol.Trim().ToUpperInvariant();
        var right = NormalizeRight(req.Right);

        items.Add(Item("symbol", "标的在白名单", s.Symbols.Contains(root),
            s.Symbols.Contains(root) ? root : $"{root} 不在 {string.Join(" / ", s.Symbols)}（个股单子胜率普遍很低）"));

        var start = ParseHm(s.EntryStart, "");
        var end = ParseHm(s.EntryEnd, "");
        var inWindow = nowEt.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                       && nowEt.TimeOfDay >= start && nowEt.TimeOfDay < end;
        items.Add(Item("window", $"美东 {s.EntryStart}–{s.EntryEnd}", inWindow, $"现在美东 {nowEt:ddd HH:mm}"));

        var ctx = await _market.ForDayAsync(DateOnly.FromDateTime(nowEt), ct);
        if (s.MaxPrevVix > 0)
            items.Add(ctx.PrevVix is { } v
                ? Item("vix", $"前日 VIX < {s.MaxPrevVix}", v < s.MaxPrevVix, $"VIX {v}（{ctx.PrevVixDate}）")
                : Item("vix", $"前日 VIX < {s.MaxPrevVix}", false, $"VIX 未取到：{ctx.Error}"));
        if (s.SkipFomcDays)
            items.Add(ctx.IsFomcDay is { } f
                ? Item("fomc", "不是 FOMC 决议日", !f, f ? "今天美联储议息" : $"下次 {ctx.NextFomc}")
                : Item("fomc", "不是 FOMC 决议日", false, $"日程未取到：{ctx.Error}"));

        var px = req.LimitPrice ?? req.EstimatedPrice ?? await OptionMidAsync(req, ct);
        var mult = req.Multiplier > 0 ? req.Multiplier : 100m;
        if (s.MaxPremium > 0)
            items.Add(px is { } p
                ? Item("premium", $"权利金 ≤ ${s.MaxPremium:0}", p * req.Quantity * mult <= s.MaxPremium,
                    $"{p:0.00} × {req.Quantity:0.##} × {mult:0} = ${p * req.Quantity * mult:0}")
                : Item("premium", $"权利金 ≤ ${s.MaxPremium:0}", false, "取不到期权报价，无法估算；请填限价"));

        if (s.RequireTrend)
            await AddTrendItemsAsync(items, root, right, s, nowEt, ct);

        var ledger = BuildLedger(nowEt);
        if (s.StopAfterLosses > 0)
            items.Add(Item("streak", $"当日连亏 < {s.StopAfterLosses} 笔", ledger.ConsecutiveLosses < s.StopAfterLosses,
                $"当前连亏 {ledger.ConsecutiveLosses} 笔"));
        if (s.DailyLossLimit > 0)
            items.Add(Item("daily", $"当日亏损未到 ${s.DailyLossLimit:0}", ledger.TodayRealized > -s.DailyLossLimit,
                $"今日已实现 {ledger.TodayRealized:+0.00;-0.00}"));
        if (s.WeeklyLossLimit > 0)
            items.Add(Item("weekly", $"本周亏损未到 ${s.WeeklyLossLimit:0}", ledger.WeekRealized > -s.WeeklyLossLimit,
                $"本周已实现 {ledger.WeekRealized:+0.00;-0.00}"));

        var failed = items.Where(i => i.Blocking && !i.Ok).ToList();
        result.Allowed = failed.Count == 0 || s.Mode != "Block";
        result.Summary = failed.Count == 0
            ? $"全部通过（{items.Count(i => i.Blocking)} 项）"
            : $"{(s.Mode == "Block" ? "拦截" : "警告")}：{string.Join("、", failed.Select(i => i.Label))}";
        return result;
    }

    private async Task AddTrendItemsAsync(List<DisciplineCheckItem> items, string root, string? right,
        DisciplineSettings s, DateTime nowEt, CancellationToken ct)
    {
        if (right is null)
        {
            items.Add(Item("trend", "顺势", false, "期权方向（C/P）缺失"));
            return;
        }
        var dir = right == "C" ? "向上" : "向下";
        try
        {
            var raw = await _hist.GetBarsAsync(root, null, "USD", "1 D", "1 min", ct);
            if (!TrendSignal.TryMapToEt(raw, nowEt, out var bars, out _, out var why))
            {
                items.Add(Item("trend", $"顺势（{dir}）", false, why));
                return;
            }
            var completed = bars.Where(b => b.StartEt.AddMinutes(1) <= nowEt).ToList();
            var m = TrendSignal.Measure(completed, right);
            if (m is null)
            {
                items.Add(Item("trend", $"顺势（{dir}）", false, "开盘区间（9:30–10:00）还没走完"));
                return;
            }
            items.Add(Item("orb", $"{root} 已{dir}突破开盘区间", m.OrbBroken,
                $"现价 {m.Price}，开盘区间 {m.OrbLow}–{m.OrbHigh}"));
            items.Add(Item("vwap", $"在 VWAP {(right == "C" ? "上方" : "下方")} ≥ {s.VwapMinPct}%", m.VwapPct >= s.VwapMinPct,
                $"VWAP {m.Vwap}，同向 {m.VwapPct:+0.00;-0.00}%"));
            items.Add(Item("range", "接近当日极值（≥ 0.8）", m.RangePos >= 0.8m,
                $"区间位置 {m.RangePos:0.00}（提示项：复盘里 ≥ 0.8 的顺势单胜率 73%）", blocking: false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            items.Add(Item("trend", $"顺势（{dir}）", false, $"取不到 {root} 分钟线：{ex.GetBaseException().Message}"));
        }
    }

    private async Task<decimal?> OptionMidAsync(PlaceOrderRequest req, CancellationToken ct)
    {
        var right = NormalizeRight(req.Right);
        var exp = req.Expiry?.Replace("-", "").Trim();
        if (right is null || req.Strike is not { } k || exp is not { Length: 8 }) return null;
        var occ = $"{req.Symbol.Trim().ToUpperInvariant()}{exp[2..]}{right}{(long)(k * 1000m):00000000}";
        try
        {
            var bars = await _hist.GetBarsAsync(occ, null, "USD", "1800 S", "1 min", ct, "OPT", "MIDPOINT");
            return bars.Count > 0 && bars[^1].Close > 0 ? bars[^1].Close : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "期权报价失败 {Occ}", occ);
            return null;
        }
    }

    private sealed record Ledger(decimal TodayRealized, decimal WeekRealized, int ConsecutiveLosses, List<DisciplineRoundTrip> Today);

    /// <summary>本系统手动单的 FIFO 多头配对（不含佣金）。</summary>
    private Ledger BuildLedger(DateTime nowEt)
    {
        var today = DateOnly.FromDateTime(nowEt);
        var weekStart = today.AddDays(-(((int)nowEt.DayOfWeek + 6) % 7));
        var lots = new Dictionary<string, Queue<(decimal Qty, decimal Px)>>();
        var trips = new List<DisciplineRoundTrip>();

        var fills = _store.List()
            .Where(o => string.IsNullOrWhiteSpace(o.Strategy) && o.FilledQuantity > 0 && o.AvgFillPrice > 0)
            .OrderBy(o => o.ClosedAt ?? o.UpdatedAt ?? o.CreatedAt);
        foreach (var o in fills)
        {
            var isOpt = o.SecType == "OPT";
            var key = isOpt ? $"{o.Symbol} {o.Expiry} {o.Strike:0.###}{o.Right}" : o.Symbol;
            var mult = o.Multiplier is > 0 ? o.Multiplier.Value : isOpt ? 100m : 1m;
            var at = o.ClosedAt ?? o.UpdatedAt ?? o.CreatedAt;
            if (!lots.TryGetValue(key, out var q)) lots[key] = q = new();
            if (o.Side == OrderSide.Buy)
            {
                q.Enqueue((o.FilledQuantity, o.AvgFillPrice));
                continue;
            }
            var remaining = o.FilledQuantity;
            decimal cost = 0, matched = 0;
            while (remaining > 0 && q.Count > 0)
            {
                var lot = q.Dequeue();
                var take = Math.Min(remaining, lot.Qty);
                cost += take * lot.Px;
                matched += take;
                remaining -= take;
                if (lot.Qty > take) q = Requeue(q, (lot.Qty - take, lot.Px), lots, key);
            }
            if (matched == 0) continue;
            trips.Add(new DisciplineRoundTrip
            {
                Contract = key,
                Quantity = matched,
                EntryPrice = decimal.Round(cost / matched, 4),
                ExitPrice = o.AvgFillPrice,
                ClosedAtUtc = at,
                Pnl = decimal.Round((o.AvgFillPrice * matched - cost) * mult, 2)
            });
        }

        DateOnly EtDay(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Ny));
        var todays = trips.Where(t => EtDay(t.ClosedAtUtc) == today).OrderBy(t => t.ClosedAtUtc).ToList();
        var streak = 0;
        foreach (var t in todays) streak = t.Pnl <= 0 ? streak + 1 : 0;
        return new Ledger(
            todays.Sum(t => t.Pnl),
            trips.Where(t => EtDay(t.ClosedAtUtc) >= weekStart && EtDay(t.ClosedAtUtc) <= today).Sum(t => t.Pnl),
            streak,
            todays);
    }

    /// <summary>部分配对后把剩余数量放回队首。</summary>
    private static Queue<(decimal, decimal)> Requeue(Queue<(decimal Qty, decimal Px)> q, (decimal, decimal) head,
        Dictionary<string, Queue<(decimal Qty, decimal Px)>> lots, string key)
    {
        var nq = new Queue<(decimal Qty, decimal Px)>();
        nq.Enqueue(head);
        foreach (var x in q) nq.Enqueue(x);
        lots[key] = nq;
        return nq;
    }

    private static DisciplineCheckItem Item(string key, string label, bool ok, string detail, bool blocking = true) =>
        new() { Key = key, Label = label, Ok = ok, Detail = detail, Blocking = blocking };

    private static string? NormalizeRight(string? r) => r?.Trim().ToUpperInvariant() switch
    {
        "C" or "CALL" => "C",
        "P" or "PUT" => "P",
        _ => null
    };

    private static TimeSpan ParseHm(string? s, string label)
    {
        if (TimeSpan.TryParseExact(s?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t)
            || TimeSpan.TryParseExact(s?.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out t))
            return t;
        throw new ArgumentException($"{label} 格式须为 HH:mm");
    }

    private static DateTime NowEt() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ny);

    private static DisciplineSettings Clone(DisciplineSettings s) =>
        JsonSerializer.Deserialize<DisciplineSettings>(JsonSerializer.Serialize(s))!;

    private static string ResolvePath()
    {
        var dir = Environment.GetEnvironmentVariable("IBTRADE_DATA_DIR");
        var baseDir = string.IsNullOrWhiteSpace(dir) ? AppContext.BaseDirectory : dir!;
        return Path.Combine(baseDir, "discipline-settings.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var s = JsonSerializer.Deserialize<DisciplineSettings>(File.ReadAllText(_path));
            if (s is not null) lock (_gate) _settings = s;
        }
        catch { /* 损坏则用默认 */ }
    }

    private void Save()
    {
        try
        {
            DisciplineSettings s;
            lock (_gate) s = _settings;
            File.WriteAllText(_path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }
}
