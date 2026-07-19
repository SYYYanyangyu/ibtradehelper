namespace IBTrade.Application.AutoTrade;

/// <summary>自动交易事件类型（用于审计日志分类）。</summary>
public enum AutoTradeEventType
{
    Info,
    Signal,
    OrderPlaced,
    OrderRejected,
    RiskBlocked,
    DryRun,
    Error
}

/// <summary>自动交易审计事件（每轮评估/每笔下产生的可追踪记录）。</summary>
public class AutoTradeEvent
{
    public DateTime Time { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public AutoTradeEventType Type { get; set; }
    public string Message { get; set; } = "";
    public string? StrategyKey { get; set; }

    /// <summary>该次评估产生的信号：Buy / Sell / Hold。</summary>
    public string? Signal { get; set; }

    /// <summary>触发来源 IP（HTTP 调用方），用于操作审计追溯。</summary>
    public string? Ip { get; set; }

    /// <summary>操作人（来自请求头 X-Operator；未提供时记为 web-client），用于操作审计。</summary>
    public string? Operator { get; set; }
}
