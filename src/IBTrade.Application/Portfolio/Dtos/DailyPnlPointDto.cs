namespace IBTrade.Application.Portfolio.Dtos;

/// <summary>每日盈亏曲线上的一个数据点。</summary>
public record DailyPnlPointDto(
    /// <summary>日期 yyyy-MM-dd</summary>
    string Date,
    /// <summary>当日账户净值（净清算值 = 持仓市值 + 现金）</summary>
    decimal NetLiquidation,
    /// <summary>当日盈亏（净值差分）= 今日净值 − 昨日净值（含外部现金流）</summary>
    decimal DailyPnl,
    /// <summary>当日盈亏百分比（基于净值差分）</summary>
    decimal DailyPnlPct,
    /// <summary>区间累计盈亏（窗口首日至当日，净值口径）</summary>
    decimal CumulativePnl,
    /// <summary>当日外部现金流（入金/出金/股息等，带符号）；用于从净值差分中扣除</summary>
    decimal ExternalCashFlow,
    /// <summary>纯交易盈亏 = 当日净值差分 − 当日外部现金流</summary>
    decimal TradingPnl,
    /// <summary>区间累计纯交易盈亏</summary>
    decimal CumulativeTradingPnl);
