using IBTrade.Application.MarketData;

namespace IBTrade.Application.Discipline;

/// <summary>
/// 手动下单的纪律闸门。只管期权开仓（买入）；平仓永远放行。
/// 默认值来自 snow 账户 FIFO 复盘与 165 天 QQQ 回测。
/// </summary>
public class DisciplineSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Block = 不满足就拒单；Warn = 只提示。</summary>
    public string Mode { get; set; } = "Block";
    public List<string> Symbols { get; set; } = ["QQQ"];
    public string EntryStart { get; set; } = "10:00";
    public string EntryEnd { get; set; } = "12:00";
    /// <summary>单笔权利金上限（价格 × 张数 × 乘数）。</summary>
    public decimal MaxPremium { get; set; } = 350m;
    public bool RequireTrend { get; set; } = true;
    public decimal VwapMinPct { get; set; } = 0.2m;
    public decimal MaxPrevVix { get; set; } = 20m;
    public bool SkipFomcDays { get; set; } = true;
    public int StopAfterLosses { get; set; } = 2;
    /// <summary>当日已实现亏损上限（美元，0 = 关）。</summary>
    public decimal DailyLossLimit { get; set; } = 350m;
    /// <summary>本周已实现亏损上限（美元，0 = 关）。</summary>
    public decimal WeeklyLossLimit { get; set; } = 1000m;
}

public class DisciplineCheckItem
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Ok { get; set; }
    /// <summary>false 时只作提示，不参与拦截。</summary>
    public bool Blocking { get; set; } = true;
    public string Detail { get; set; } = "";
}

public class DisciplineCheckResult
{
    public bool Applies { get; set; }
    public bool Allowed { get; set; } = true;
    public string Mode { get; set; } = "Block";
    public string Summary { get; set; } = "";
    public List<DisciplineCheckItem> Items { get; set; } = [];
}

public class DisciplineRoundTrip
{
    public string Contract { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public DateTime ClosedAtUtc { get; set; }
    public decimal Pnl { get; set; }
}

public class DisciplineStatus
{
    public required DisciplineSettings Settings { get; init; }
    public string TodayEt { get; set; } = "";
    public decimal TodayRealized { get; set; }
    public int TodayTrades { get; set; }
    public int ConsecutiveLosses { get; set; }
    public decimal WeekRealized { get; set; }
    public MarketContext Context { get; set; } = new();
    public string Scope { get; set; } = "只统计通过本系统手动下单的成交（不含 IBKR App/TWS 直接下的单，也不含佣金）";
    public List<DisciplineRoundTrip> Today { get; set; } = [];
}
