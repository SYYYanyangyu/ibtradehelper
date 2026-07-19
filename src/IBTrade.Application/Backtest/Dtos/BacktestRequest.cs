namespace IBTrade.Application.Backtest.Dtos;

/// <summary>
/// 回测请求参数。数据从 MarketData（IB Gateway 历史 K 线）拉取，策略与撮合在内存完成。
/// </summary>
public class BacktestRequest
{
    /// <summary>标的代码，例如 AAPL。</summary>
    public string Symbol { get; set; } = "";

    /// <summary>交易所，默认 SMART。</summary>
    public string? Exchange { get; set; }

    /// <summary>计价货币，默认 USD。</summary>
    public string? Currency { get; set; } = "USD";

    /// <summary>历史区间长度，例如 "1 Y"、"6 M"、"60 D"。</summary>
    public string Duration { get; set; } = "1 Y";

    /// <summary>K 线粒度，例如 "1 day"、"1 hour"、"5 mins"。</summary>
    public string BarSize { get; set; } = "1 day";

    /// <summary>策略标识，默认 sma_cross（均线交叉）。</summary>
    public string Strategy { get; set; } = "sma_cross";

    /// <summary>策略参数（如 {"fast":10,"slow":30}），不同策略含义不同。</summary>
    public Dictionary<string, double>? Parameters { get; set; }

    /// <summary>初始资金。</summary>
    public decimal InitialCapital { get; set; } = 100_000m;

    /// <summary>每笔交易佣金（固定额，简化模型）。</summary>
    public decimal CommissionPerTrade { get; set; } = 1m;

    /// <summary>
    /// 成交价模型：
    /// next_open（默认）= 信号当根收盘后，下一根开盘成交（避免同 bar 前瞻偏差）；
    /// close = 旧模型，信号当根收盘成交（仅对比用）。
    /// </summary>
    public string FillMode { get; set; } = "next_open";

    /// <summary>单边滑点（基点）。买入上浮、卖出下浮。默认 5 bps = 0.05%。</summary>
    public decimal SlippageBps { get; set; } = 5m;
}
