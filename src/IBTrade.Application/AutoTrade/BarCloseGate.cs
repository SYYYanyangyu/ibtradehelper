using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.AutoTrade;

/// <summary>
/// Bar-close 对齐：避免在未收盘的最后一根 K 线上交易（消除盘中「假信号」）。
/// 规则：若最后一根尚未走完周期 → 用倒数第二根；否则用最后一根。
/// </summary>
public static class BarCloseGate
{
    /// <summary>
    /// 选出用于决策的已收盘（或判定已收盘）K 线索引与该棒时间。
    /// 返回 null 表示数据不足以做 bar-close 决策。
    /// </summary>
    public static (int Index, CandleDto Bar, bool UsedPenultimate)? ResolveSignalBar(
        IReadOnlyList<CandleDto> candles, string barSize, DateTime? nowUtc = null)
    {
        if (candles.Count < 2) return null;

        var now = nowUtc ?? DateTime.UtcNow;
        var last = candles[^1];
        var lastClosed = IsLikelyClosed(last.Time, barSize, now);

        if (lastClosed)
            return (candles.Count - 1, last, false);

        if (candles.Count < 3)
            return null; // 仅有形成中的 bar + 一根历史，宁可跳过也不用未收盘 bar

        return (candles.Count - 2, candles[^2], true);
    }

    /// <summary>
    /// 粗判该 bar 是否已走完。IB 日线时间多为当日 00:00 或收盘时刻，故用「bar 起点 + 周期」保守判断。
    /// </summary>
    public static bool IsLikelyClosed(DateTime barTime, string barSize, DateTime nowUtc)
    {
        var duration = ParseDuration(barSize);
        // 给 30 秒缓冲，避免边界抖动
        var closeAt = barTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(barTime, DateTimeKind.Utc).Add(duration)
            : barTime.ToUniversalTime().Add(duration);
        return nowUtc >= closeAt.AddSeconds(-30);
    }

    public static TimeSpan ParseDuration(string barSize)
    {
        var b = (barSize ?? "").Trim().ToLowerInvariant();
        if (b.Contains("day")) return TimeSpan.FromDays(1);
        if (b.Contains("hour")) return TimeSpan.FromHours(1);
        if (b.Contains("min"))
        {
            var digits = new string(b.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var m) && m > 0) return TimeSpan.FromMinutes(m);
            return TimeSpan.FromMinutes(5);
        }
        return TimeSpan.FromDays(1);
    }
}
