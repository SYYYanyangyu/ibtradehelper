namespace IBTrade.Application.Backtest.Dtos;

/// <summary>单笔已平仓交易记录。</summary>
public record TradeRecord(
    DateTime EntryTime,
    decimal EntryPrice,
    DateTime ExitTime,
    decimal ExitPrice,
    decimal Quantity,
    decimal ProfitLoss,
    decimal ReturnPct,
    /// <summary>该笔往返佣金合计。</summary>
    decimal Commission = 0m,
    /// <summary>该笔往返滑点成本（相对无滑点参考价）。</summary>
    decimal SlippageCost = 0m);

/// <summary>资金曲线上的一个点。</summary>
public record EquityPoint(DateTime Time, decimal Equity);

/// <summary>回测结果：绩效指标 + 成本分解 + 交易明细 + 资金曲线。</summary>
public class BacktestResult
{
    public string Symbol { get; set; } = "";
    public string Strategy { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int BarCount { get; set; }

    public decimal InitialCapital { get; set; }
    public decimal FinalEquity { get; set; }

    /// <summary>策略总收益率（%，已含佣金与滑点）。</summary>
    public decimal TotalReturnPct { get; set; }

    /// <summary>同期买入持有收益率（%），作为对照基准（不含交易成本）。</summary>
    public decimal BuyHoldReturnPct { get; set; }

    /// <summary>最大回撤（%，正数表示回撤幅度）。</summary>
    public decimal MaxDrawdownPct { get; set; }

    public int TradeCount { get; set; }
    public int WinCount { get; set; }

    /// <summary>胜率（%）。</summary>
    public decimal WinRatePct { get; set; }

    /// <summary>年化夏普比率（基于每根 K 线收益，简化估算）。</summary>
    public decimal Sharpe { get; set; }

    // ---- 成本模型回显 ----

    /// <summary>next_open | close</summary>
    public string FillMode { get; set; } = "next_open";

    public decimal SlippageBps { get; set; }
    public decimal CommissionPerTrade { get; set; }

    /// <summary>累计佣金（进出各一笔之和）。</summary>
    public decimal TotalCommission { get; set; }

    /// <summary>累计滑点成本（相对无滑点参考价的不利差额）。</summary>
    public decimal TotalSlippageCost { get; set; }

    /// <summary>总成本 = 佣金 + 滑点。</summary>
    public decimal TotalCost { get; set; }

    /// <summary>成本拖累占初始资金比例（%）。</summary>
    public decimal CostDragPct { get; set; }

    /// <summary>
    /// 近似毛收益（%）：在净收益上加回总成本（忽略复利路径差异，用于直观看成本影响）。
    /// </summary>
    public decimal ApproxGrossReturnPct { get; set; }

    public List<TradeRecord> Trades { get; set; } = new();
    public List<EquityPoint> EquityCurve { get; set; } = new();
}
