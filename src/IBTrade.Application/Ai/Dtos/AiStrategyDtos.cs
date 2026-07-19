using IBTrade.Application.Backtest.Dtos;

namespace IBTrade.Application.Ai.Dtos;

/// <summary>自然语言 → 策略建议请求。</summary>
public class AiSuggestRequest
{
    /// <summary>用户意图，例如「用均线做 AAPL 中长线，偏保守」。</summary>
    public string Prompt { get; set; } = "";

    /// <summary>可选：已指定标的；未指定时由顾问从 prompt 推断或默认 AAPL。</summary>
    public string? Symbol { get; set; }

    public string? Duration { get; set; }
    public string? BarSize { get; set; }
}

/// <summary>AI/启发式策略建议（绝不含可执行代码）。</summary>
public class AiSuggestResult
{
    public string Symbol { get; set; } = "";
    public string StrategyKey { get; set; } = "";
    public string StrategyName { get; set; } = "";
    public Dictionary<string, double> Parameters { get; set; } = new();
    public string Duration { get; set; } = "1 Y";
    public string BarSize { get; set; } = "1 day";
    /// <summary>heuristic | llm</summary>
    public string Source { get; set; } = "heuristic";
    public string Rationale { get; set; } = "";
    public double Confidence { get; set; }
}

/// <summary>建议 + 立即回测。</summary>
public class AiSuggestAndRunRequest : AiSuggestRequest
{
    public decimal InitialCapital { get; set; } = 100_000m;
    public decimal CommissionPerTrade { get; set; } = 1m;
    /// <summary>next_open（默认）| close</summary>
    public string FillMode { get; set; } = "next_open";
    public decimal SlippageBps { get; set; } = 5m;
}

public class AiSuggestAndRunResult
{
    public AiSuggestResult Suggestion { get; set; } = new();
    public BacktestResult Backtest { get; set; } = null!;
}

/// <summary>回测参数一键部署到自动交易（强制 DryRun）。</summary>
public class DeployDryRunRequest
{
    public string Symbol { get; set; } = "";
    public string StrategyKey { get; set; } = "";
    public Dictionary<string, double>? Parameters { get; set; }
    public string? Duration { get; set; }
    public string? BarSize { get; set; }
    /// <summary>演练每笔股数，默认 100。</summary>
    public decimal Quantity { get; set; } = 100m;
    /// <summary>是否同时启动引擎轮询（仍不会真实下单，因 DryRun=true）。</summary>
    public bool StartEngine { get; set; }

    /// <summary>部署前已确认警告项（有 warn 时必须为 true，否则拒绝）。</summary>
    public bool AcknowledgeWarnings { get; set; }

    /// <summary>可选：附带回测摘要供服务端再次门禁（与 preflight 一致）。</summary>
    public string? FillMode { get; set; }
    public decimal? SlippageBps { get; set; }
    public decimal? TotalReturnPct { get; set; }
    public decimal? BuyHoldReturnPct { get; set; }
    public decimal? MaxDrawdownPct { get; set; }
    public decimal? Sharpe { get; set; }
    public int? TradeCount { get; set; }
    public int? BarCount { get; set; }
    public decimal? CostDragPct { get; set; }
    public decimal? TotalCost { get; set; }
}

public class DeployDryRunResult
{
    public string Symbol { get; set; } = "";
    public string StrategyKey { get; set; } = "";
    public Dictionary<string, double> Parameters { get; set; } = new();
    public bool DryRun { get; set; } = true;
    public bool EngineRunning { get; set; }
    /// <summary>当前风控交易模式：Paper | Live（部署本身仍强制 DryRun）。</summary>
    public string RiskMode { get; set; } = "Paper";
    public bool LiveAcknowledged { get; set; }
    public string Message { get; set; } = "";
}
