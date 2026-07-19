namespace IBTrade.Application.Ai.Dtos;

/// <summary>部署前检查请求（可附带回测摘要做质量门禁）。</summary>
public class DeployPreflightRequest
{
    public string Symbol { get; set; } = "";
    public string StrategyKey { get; set; } = "";
    public Dictionary<string, double>? Parameters { get; set; }
    public string? Duration { get; set; }
    public string? BarSize { get; set; }
    public decimal Quantity { get; set; } = 100m;

    /// <summary>回测撮合模型：next_open | close（可选）。</summary>
    public string? FillMode { get; set; }
    public decimal? SlippageBps { get; set; }

    // ---- 可选回测摘要（有则做质量警告）----
    public decimal? TotalReturnPct { get; set; }
    public decimal? BuyHoldReturnPct { get; set; }
    public decimal? MaxDrawdownPct { get; set; }
    public decimal? Sharpe { get; set; }
    public int? TradeCount { get; set; }
    public int? BarCount { get; set; }
    public int? WinCount { get; set; }

    /// <summary>成本拖累占初始资金 %（回测结果）。</summary>
    public decimal? CostDragPct { get; set; }
    public decimal? TotalCost { get; set; }
}

public class DeployCheckItem
{
    /// <summary>稳定 id，便于前端定位。</summary>
    public string Id { get; set; } = "";

    /// <summary>pass | warn | block | info</summary>
    public string Level { get; set; } = "pass";

    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class DeployPreflightResult
{
    /// <summary>无 block 项时可部署。</summary>
    public bool CanDeploy { get; set; }

    /// <summary>存在 warn，部署需 AcknowledgeWarnings=true。</summary>
    public bool HasWarnings { get; set; }

    public string Summary { get; set; } = "";
    public List<DeployCheckItem> Checks { get; set; } = new();

    // 消毒后的配置回显
    public string Symbol { get; set; } = "";
    public string StrategyKey { get; set; } = "";
    public string StrategyName { get; set; } = "";
    public Dictionary<string, double> Parameters { get; set; } = new();
    public string Duration { get; set; } = "";
    public string BarSize { get; set; } = "";
    public decimal Quantity { get; set; }
    public string RiskMode { get; set; } = "Paper";
    public bool LiveAcknowledged { get; set; }
    public bool EngineRunning { get; set; }
    public bool MasterEnabled { get; set; }
    public bool CompletedBarsOnly { get; set; } = true;
}
