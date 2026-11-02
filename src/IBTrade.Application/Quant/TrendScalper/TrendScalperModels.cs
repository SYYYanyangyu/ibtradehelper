namespace IBTrade.Application.Quant.TrendScalper;

/// <summary>
/// QQQ 0DTE 顺势短线（只 Paper）。默认参数来自 snow 账户 FIFO 复盘：
/// 开盘区间同向突破 + VWAP 同侧 + 接近当日极值时进场；止盈止损按 QQQ 位移计。
/// </summary>
public class TrendScalperSettings
{
    // Opt-in candidates; false preserves the baseline signal and exits.
    public bool RequireRetest { get; set; }
    public int RetestLookback { get; set; } = 10;
    public decimal RetestTolerancePct { get; set; } = 0.03m;
    public bool UseAtrExits { get; set; }
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiple { get; set; } = 1m;
    public decimal AtrTargetMultiple { get; set; } = 1.5m;
    public decimal EstimatedCommissionPerContractSide { get; set; } = 0.65m;
    public string Underlying { get; set; } = "QQQ";
    /// <summary>单笔权利金上限（美元，按 限价 × 100 × 张数）。</summary>
    public decimal BudgetPerTrade { get; set; } = 350m;
    /// <summary>当日已实现亏损上限（美元，0 = 关闭）。剩余额度不足单笔预算时按剩余额度缩小仓位。</summary>
    public decimal DailyLossLimit { get; set; } = 350m;
    /// <summary>用限价单（mid ± 缓冲）开平仓；关闭则用市价单。</summary>
    public bool UseLimitOrders { get; set; } = true;
    /// <summary>限价相对 mid 的缓冲（%），至少 $0.02。</summary>
    public decimal LimitBufferPct { get; set; } = 3m;
    public int MaxContracts { get; set; } = 3;
    /// <summary>止盈：QQQ 同向位移（%）。</summary>
    public decimal TakeProfitPct { get; set; } = 0.10m;
    /// <summary>止损：QQQ 反向位移（%）。</summary>
    public decimal StopLossPct { get; set; } = 0.10m;
    public int MaxHoldMinutes { get; set; } = 15;
    /// <summary>价格须在 VWAP 同侧至少该幅度（%）。</summary>
    public decimal VwapMinPct { get; set; } = 0.2m;
    /// <summary>当日区间位置下限（0–1，1 = 方向上的极值）。</summary>
    public decimal RangePosMin { get; set; } = 0.8m;
    /// <summary>美东开仓窗口（HH:mm）。</summary>
    public string EntryStart { get; set; } = "10:00";
    public string EntryEnd { get; set; } = "13:00";
    /// <summary>美东强平时间（HH:mm）。0DTE 不过夜。</summary>
    public string FlatBy { get; set; } = "15:45";
    public int MaxTradesPerDay { get; set; } = 6;
    public int StopAfterLosses { get; set; } = 2;
    public bool AllowCalls { get; set; } = true;
    public bool AllowPuts { get; set; } = true;
    public int PollSeconds { get; set; } = 20;
    /// <summary>前一交易日 VIX 收盘（FRED VIXCLS）≥ 该值则不开仓；0 = 关闭。回测中 VIX ≥ 20 时追势利润因子 0.27–0.40。</summary>
    public decimal MaxPrevVix { get; set; } = 20m;
    /// <summary>美联储议息决议日不开仓（日程取自 federalreserve.gov）。</summary>
    public bool SkipFomcDays { get; set; } = true;
}

public class TrendScalperPosition
{
    // Captured at entry; later settings changes do not move ATR risk boundaries.
    public decimal? EntryStopPct { get; set; }
    public decimal? EntryTargetPct { get; set; }
    public decimal? CommissionPerContractSide { get; set; }
    public string Occ { get; set; } = "";
    public string Expiry { get; set; } = "";
    public decimal Strike { get; set; }
    public string Right { get; set; } = "C";
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    /// <summary>下单前的期权 mid，用于统计滑点。</summary>
    public decimal? EntryMid { get; set; }
    public decimal EntryUnderlying { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public string EntryReason { get; set; } = "";
    public string? EntryOrderId { get; set; }
    /// <summary>首次决定平仓时的期权 mid；重试不覆盖，滑点包含重试成本。</summary>
    public decimal? ExitMid { get; set; }
    public int ExitAttempts { get; set; }
    /// <summary>限价平仓部分成交时已卖出的张数与金额（每股价 × 张数）。</summary>
    public decimal ClosedQuantity { get; set; }
    public decimal ClosedProceeds { get; set; }
    /// <summary>平仓单已发出但未确认成交时记录，避免重复卖出。</summary>
    public string? PendingExitOrderId { get; set; }
    public string? PendingExitReason { get; set; }
    public decimal? PendingExitUnderlying { get; set; }
}

public class TrendScalperTrade
{
    public decimal? EstimatedCommission { get; set; }
    public decimal? EstimatedNetPnl => EstimatedCommission is { } cost ? Pnl - cost : null;
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string DateEt { get; set; } = "";
    public string Occ { get; set; } = "";
    public string Right { get; set; } = "C";
    public decimal Strike { get; set; }
    public decimal Quantity { get; set; }
    public DateTime EntryAtUtc { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal EntryUnderlying { get; set; }
    public DateTime ExitAtUtc { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal ExitUnderlying { get; set; }
    /// <summary>tp / sl / time / eod / manual / data</summary>
    public string ExitReason { get; set; } = "";
    public string EntryReason { get; set; } = "";
    /// <summary>(平仓价 − 开仓价) × 张数 × 100，不含佣金。</summary>
    public decimal Pnl { get; set; }
    public decimal? EntryMid { get; set; }
    public decimal? ExitMid { get; set; }
    /// <summary>相对下单前 mid 多付 / 少收的金额（美元，正数 = 成本）；mid 取自最近 1 分钟 MIDPOINT K 线。</summary>
    public decimal? Slippage { get; set; }
}

public class TrendScalperDay
{
    public string DateEt { get; set; } = "";
    public int Trades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int ConsecutiveLosses { get; set; }
    public decimal Realized { get; set; }
}

public class TrendScalperSnapshot
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string? BarEt { get; set; }
    public decimal? Price { get; set; }
    public decimal? Vwap { get; set; }
    public decimal? OrbHigh { get; set; }
    public decimal? OrbLow { get; set; }
    public decimal? DayHigh { get; set; }
    public decimal? DayLow { get; set; }
    /// <summary>buy-call / buy-put / hold / exit</summary>
    public string Decision { get; set; } = "hold";
    public string Reason { get; set; } = "";
}

public class TrendScalperStats
{
    public int CostedTrades { get; set; }
    public decimal? EstimatedNetExpectancy { get; set; }

    public int Trades { get; set; }
    public int Wins { get; set; }
    public decimal WinRate { get; set; }
    public decimal Pnl { get; set; }
    public decimal? ProfitFactor { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public int SlippageTrades { get; set; }
    public decimal TotalSlippage { get; set; }
    public decimal? AvgSlippage { get; set; }
}

public class TrendScalperStatus
{
    public bool Running { get; set; }
    public required TrendScalperSettings Settings { get; init; }
    public string RiskMode { get; set; } = "Paper";
    public string Session { get; set; } = "";
    public TrendScalperPosition? Position { get; set; }
    public decimal? LastUnderlying { get; set; }
    public MarketData.MarketContext Context { get; set; } = new();
    public TrendScalperDay Today { get; set; } = new();
    public TrendScalperStats Total { get; set; } = new();
    public TrendScalperSnapshot? Last { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastTickUtc { get; set; }
    public required IReadOnlyList<TrendScalperTrade> Trades { get; init; }
    public required IReadOnlyList<TrendScalperSnapshot> Log { get; init; }
}
