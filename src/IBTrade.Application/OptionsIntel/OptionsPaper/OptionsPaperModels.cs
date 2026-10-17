using IBTrade.Application.OptionsIntel.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.OptionsIntel.OptionsPaper;

public class OptionsPaperSettings
{
    public string Symbol { get; set; } = "AAPL";
    public decimal Quantity { get; set; } = 10;
    public int PollSeconds { get; set; } = 90;
    /// <summary>预测概率变动阈值（绝对值，0.03 = 3 个百分点）。第一次采样只建基线，不交易。</summary>
    public decimal MinProbMove { get; set; } = 0.03m;
    /// <summary>尝试向 IB Gateway Paper 发单。失败（周末/未连）则按最新官方现价本地记账。</summary>
    public bool SendToGateway { get; set; } = true;
}

public class OptionsPaperSignal
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public string Bias { get; set; } = "hold";
    public string Reason { get; set; } = "";
    public string? MarketQuestion { get; set; }
    public string? Venue { get; set; }
    public decimal? Probability { get; set; }
    public decimal? Change { get; set; }
    public decimal? PutCallOiRatio { get; set; }
    public decimal? Spot { get; set; }
}

public class OptionsPaperFill
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public string Venue { get; set; } = "paper-sim";
    public string? BrokerOrderId { get; set; }
    public string Reason { get; set; } = "";
    public string? Error { get; set; }
}

public class OptionsPaperStatus
{
    public bool Running { get; set; }
    public required OptionsPaperSettings Settings { get; init; }
    public string AccountHint { get; set; } = "Paper DUN716484";
    public string RiskMode { get; set; } = "Paper";
    public decimal NetQty { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal? Mark { get; set; }
    public decimal Unrealized { get; set; }
    public decimal Realized { get; set; }
    public decimal TotalPnl { get; set; }
    public int SignalCount { get; set; }
    public int FillCount { get; set; }
    public OptionsPaperSignal? LastSignal { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastTickUtc { get; set; }
    public required IReadOnlyList<OptionsPaperSignal> Signals { get; init; }
    public required IReadOnlyList<OptionsPaperFill> Fills { get; init; }
    public required IReadOnlyList<PredictionMarketDto> Markets { get; init; }
}
