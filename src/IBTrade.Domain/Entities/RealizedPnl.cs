using IBTrade.Domain.Common;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 按标的的已实现/未实现盈亏（来自 IBKR Flex "Realized and Unrealized Performance Summary"）。
/// 以 (AccountId, Symbol) 为业务维度，每次接入整批替换，便于直接回答"历史每只股票赚/亏多少"。
/// </summary>
public class RealizedPnl : Entity
{
    /// <summary>IBKR 账户号</summary>
    public required string AccountId { get; set; }

    /// <summary>标的代码（如 IFNNY、PLUG）</summary>
    public required string Symbol { get; set; }

    /// <summary>资产类别（Flex 原始值，如 STK/OPT）</summary>
    public string AssetCategory { get; set; } = "STK";

    /// <summary>标的描述</summary>
    public string? Description { get; set; }

    /// <summary>币种</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>已实现盈亏（基础币种，来自 fifoPnlRealized）</summary>
    public decimal Realized { get; set; }

    /// <summary>未实现盈亏（基础币种，来自 mtmPnl）</summary>
    public decimal Unrealized { get; set; }

    /// <summary>最近一次接入时间（UTC），用于判断数据新鲜度</summary>
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;
}
