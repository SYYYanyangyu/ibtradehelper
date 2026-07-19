using IBTrade.Domain.Enums;

namespace IBTrade.Application.AutoTrade;

/// <summary>自动交易引擎全局设置（运行时可调，重启不持久化）。</summary>
public class AutoTradeSettings
{
    /// <summary>
    /// 引擎总开关：false 时后台循环空转、不评估任何标的。默认 false —— 必须显式开启，避免误触。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>全局轮询周期（秒）。每轮遍历所有启用的标的并评估一次。默认 120，避免频繁拉数据触发 IBKR 频率限制。</summary>
    public int PollIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// 仅在已收盘 K 线上决策（默认 true）。关闭后回退为「用最后一根」，仅供对比调试。
    /// </summary>
    public bool UseCompletedBarsOnly { get; set; } = true;
}

/// <summary>单个标的的自动交易订阅配置。</summary>
public class AutoTradeSymbolConfig
{
    public string Symbol { get; set; } = "";

    /// <summary>策略 key：sma_cross / rsi / donchian。</summary>
    public string StrategyKey { get; set; } = "sma_cross";

    /// <summary>策略参数（与回测一致）。</summary>
    public Dictionary<string, double> Parameters { get; set; } = new();

    /// <summary>K 线粒度（须为 IBKR 合法值）。</summary>
    public string BarSize { get; set; } = "1 day";

    /// <summary>回看区间。</summary>
    public string Duration { get; set; } = "1 Y";

    /// <summary>每笔买入数量（卖出平仓数量使用当前净持仓）。</summary>
    public decimal Quantity { get; set; } = 100;

    public OrderType OrderType { get; set; } = OrderType.Market;

    /// <summary>限价单价格；市价单忽略。</summary>
    public decimal? LimitPrice { get; set; }

    /// <summary>是否参与自动交易（关闭则跳过）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 演练模式：true = 只产生信号与下单意图、绝不真正下单（默认 true，安全第一）。
    /// 确认策略信号无误后再关闭，才会真正向 Gateway 发单。
    /// </summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>单个标的运行时状态（供前端展示与审计；基于本系统自身订单推导持仓）。</summary>
public class AutoTradeSymbolRuntime
{
    public string Symbol { get; set; } = "";
    public string? StrategyKey { get; set; }

    /// <summary>最近一次策略信号：Buy / Sell / Hold。</summary>
    public string? LastSignal { get; set; }

    /// <summary>产生该信号的已收盘 K 线时间。</summary>
    public DateTime? SignalBarTime { get; set; }

    /// <summary>是否因最后一根未收盘而改用了倒数第二根。</summary>
    public bool UsedPenultimateBar { get; set; }

    /// <summary>基于本系统成交订单推导的净持仓（正=多头，负=空头，0=空仓）。</summary>
    public decimal NetQty { get; set; }

    /// <summary>该标的当前未完结订单数。</summary>
    public int OpenOrders { get; set; }

    public DateTime? LastEvaluatedAt { get; set; }

    /// <summary>最近一次执行的动作描述（含演练）。</summary>
    public string? LastAction { get; set; }

    public string? LastError { get; set; }
}
