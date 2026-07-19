namespace IBTrade.Application.Portfolio;

/// <summary>
/// 交易日历：用于"今日盈亏"等场景正确处理周末/非交易日。
/// 当前按周一至周五判定（暂未剔除法定节假日）；如需节假日可在此扩展。
/// </summary>
public static class TradingCalendar
{
    /// <summary>是否为交易日：周一至周五。</summary>
    public static bool IsTradingDay(DateTime day)
    {
        var dow = day.Date.DayOfWeek;
        return dow is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    /// <summary>
    /// 返回不晚于 given 的最近交易日（含 given 当天；若为周末则回退到周五）。
    /// 仅用于<strong>展示/查询对齐</strong>（如 Hero 标「最近交易日」）。
    /// 切勿在非交易日把实时估值写回该日期——会覆盖已收盘快照（见 <see cref="CanMutateDailySnapshot"/>）。
    /// </summary>
    public static DateTime LatestTradingDay(DateTime given)
    {
        var d = given.Date;
        while (!IsTradingDay(d)) d = d.AddDays(-1);
        return d;
    }

    /// <summary>
    /// 当前时刻是否允许写入/覆盖「日净值快照」。
    /// 周末等非交易日禁止：TWS 仍会返回上周五标记价，若写回 LatestTradingDay 会污染已收盘日的今日盈亏。
    /// 显式指定 asOf 回填历史时由调用方自行决定，不经过此门闩。
    /// </summary>
    public static bool CanMutateDailySnapshot(DateTime utcNow) => IsTradingDay(utcNow);
}
