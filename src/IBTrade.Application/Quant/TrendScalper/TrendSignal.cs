using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.Quant.TrendScalper;

/// <summary>美东时间的 1 分钟 K 线（StartEt = 该分钟开始时刻）。</summary>
public readonly record struct EtBar(DateTime StartEt, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>某一方向（C 看涨 / P 看跌）上的顺势度量。</summary>
public record TrendMeasure(
    string Right,
    bool OrbBroken,
    decimal VwapPct,
    decimal RangePos,
    decimal Price,
    decimal Vwap,
    decimal OrbHigh,
    decimal OrbLow,
    decimal DayHigh,
    decimal DayLow,
    string BarEt);

public static class TrendSignal
{
    private static readonly TimeSpan OrbEnd = new(10, 0, 0);
    private static readonly TimeSpan SessionOpen = new(9, 30, 0);

    /// <summary>
    /// Gateway 返回的 K 线时间是登录时区的墙钟时间。当日 RTH 首根必为 9:30 ET，据此换算时差；
    /// 最后一根离当前超过 3 分钟则视为非当日数据（节假日 / 断线）。
    /// </summary>
    public static bool TryMapToEt(IReadOnlyList<CandleDto> raw, DateTime nowEt,
        out List<EtBar> bars, out TimeSpan offset, out string why)
    {
        bars = [];
        offset = TimeSpan.Zero;
        if (raw.Count == 0) { why = "Gateway 没有返回 K 线"; return false; }
        var first = raw[0].Time;
        if (first.Minute != 30) { why = $"首根 K 线 {first:HH:mm} 不是 9:30 开盘，无法对齐美东时间"; return false; }
        offset = first - nowEt.Date.Add(SessionOpen);
        foreach (var c in raw)
            bars.Add(new EtBar(c.Time - offset, c.Open, c.High, c.Low, c.Close, c.Volume));
        var lag = nowEt - bars[^1].StartEt;
        if (lag > TimeSpan.FromMinutes(3) || lag < TimeSpan.FromMinutes(-2))
        {
            why = $"K 线不是今天的实时数据（最后一根对应美东 {bars[^1].StartEt:HH:mm}，现在 {nowEt:HH:mm}）";
            return false;
        }
        why = "";
        return true;
    }

    /// <summary>开盘区间（9:30–10:00）未走完时返回 null。</summary>
    public static TrendMeasure? Measure(IReadOnlyList<EtBar> bars, string right)
    {
        var orb = bars.Where(b => b.StartEt.TimeOfDay < OrbEnd).ToList();
        if (orb.Count < 30 || bars.Count <= orb.Count) return null;

        decimal pv = 0, vol = 0;
        foreach (var b in bars)
        {
            pv += (b.High + b.Low + b.Close) / 3m * b.Volume;
            vol += b.Volume;
        }
        var last = bars[^1];
        var px = last.Close;
        var vwap = vol > 0 ? pv / vol : px;
        var hi = bars.Max(b => b.High);
        var lo = bars.Min(b => b.Low);
        var orh = orb.Max(b => b.High);
        var orl = orb.Min(b => b.Low);
        var range = hi - lo;
        var sgn = right == "C" ? 1m : -1m;
        return new TrendMeasure(
            right,
            right == "C" ? px > orh : px < orl,
            sgn * (px / vwap - 1m) * 100m,
            range <= 0 ? 0m : right == "C" ? (px - lo) / range : (hi - px) / range,
            px, decimal.Round(vwap, 3), orh, orl, hi, lo,
            last.StartEt.ToString("HH:mm"));
    }

    /// <summary>
    /// 只看已收盘的当日 K 线。返回 C / P / null。
    /// 条件（与复盘、回测一致）：开盘区间（9:30–10:00）同向突破、价格在 VWAP 同侧 ≥ VwapMinPct、
    /// 当日区间位置 ≥ RangePosMin（Call 接近高点 / Put 接近低点）。
    /// </summary>
    public static (string? Right, TrendScalperSnapshot Snapshot) Evaluate(IReadOnlyList<EtBar> bars, TrendScalperSettings s)
    {
        var snap = new TrendScalperSnapshot();
        var reasons = new List<string>();
        foreach (var right in new[] { "C", "P" })
        {
            var m = Measure(bars, right);
            if (m is null)
            {
                var n = bars.Count(b => b.StartEt.TimeOfDay < OrbEnd);
                snap.Reason = $"开盘区间未走完（{n}/30 根）";
                return (null, snap);
            }
            snap.BarEt = m.BarEt;
            snap.Price = m.Price;
            snap.Vwap = m.Vwap;
            snap.OrbHigh = m.OrbHigh;
            snap.OrbLow = m.OrbLow;
            snap.DayHigh = m.DayHigh;
            snap.DayLow = m.DayLow;

            if (right == "C" && !s.AllowCalls) continue;
            if (right == "P" && !s.AllowPuts) continue;
            var label = right == "C" ? "Call" : "Put";
            if (m.OrbBroken && m.VwapPct >= s.VwapMinPct && m.RangePos >= s.RangePosMin)
            {
                snap.Decision = right == "C" ? "buy-call" : "buy-put";
                snap.Reason = $"{label}：突破开盘区间{(right == "C" ? $"高点 {m.OrbHigh}" : $"低点 {m.OrbLow}")}，" +
                              $"VWAP 同侧 {m.VwapPct:0.00}%，区间位置 {m.RangePos:0.00}";
                return (right, snap);
            }
            var miss = new List<string>();
            if (!m.OrbBroken) miss.Add("未突破开盘区间");
            if (m.VwapPct < s.VwapMinPct) miss.Add($"VWAP {m.VwapPct:+0.00;-0.00}%");
            if (m.RangePos < s.RangePosMin) miss.Add($"区间位置 {m.RangePos:0.00}");
            reasons.Add($"{label}：{string.Join("、", miss)}");
        }
        snap.Reason = reasons.Count == 0 ? "Call / Put 都已关闭" : string.Join("；", reasons);
        return (null, snap);
    }
}
