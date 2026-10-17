using IBTrade.Domain.Common;
using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 持仓：某账户在某标的上的当前持仓快照。
/// </summary>
public class Position : Entity
{
    /// <summary>IBKR 账户号</summary>
    public required string AccountId { get; set; }

    /// <summary>标的代码，如 AAPL</summary>
    public required string Symbol { get; set; }

    /// <summary>资产类型</summary>
    public AssetType AssetType { get; set; }

    /// <summary>币种，如 USD</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>持仓数量（可为负，表示做空）</summary>
    public decimal Quantity { get; set; }

    /// <summary>平均成本价</summary>
    public decimal AvgCost { get; set; }

    /// <summary>最新市场价（每股/每单位权利金，不含乘数）</summary>
    public decimal MarketPrice { get; set; }

    /// <summary>合约乘数：正股 1，美股期权通常 100。</summary>
    public decimal Multiplier { get; set; } = 1m;

    /// <summary>
    /// 仓位结构来源（数量/均价）：Flex = 报表权威；Gateway = 仅网关会话写入的仓位。
    /// 遗留空值按 Flex 处理，避免 Gateway 同步误删报表仓位。
    /// </summary>
    public string Source { get; set; } = DataSources.Flex;

    /// <summary>市价最近一次由谁刷新：Flex 或 Gateway。</summary>
    public string PriceSource { get; set; } = DataSources.Flex;

    public decimal EffectiveMultiplier => Multiplier > 0 ? Multiplier : 1m;

    /// <summary>市值 = 数量 × 市场价 × 乘数</summary>
    public decimal MarketValue => Quantity * MarketPrice * EffectiveMultiplier;

    /// <summary>未实现盈亏 = (市场价 − 成本价) × 数量 × 乘数</summary>
    public decimal UnrealizedPnl => (MarketPrice - AvgCost) * Quantity * EffectiveMultiplier;
}
