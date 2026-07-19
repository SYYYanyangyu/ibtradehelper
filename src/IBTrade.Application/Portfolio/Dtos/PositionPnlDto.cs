using IBTrade.Domain.Entities;

namespace IBTrade.Application.Portfolio.Dtos;

/// <summary>
/// 单标的盈亏明细：合并未实现（来自持仓快照）与已实现（来自成交历史 FIFO 配对）。
/// </summary>
public record PositionPnlDto(
    long Id,
    string AccountId,
    string Symbol,
    string AssetType,
    string Currency,
    decimal Quantity,
    decimal AvgCost,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    /// <summary>当前持仓成本 = 平均成本 × 数量</summary>
    decimal CostBasis,
    /// <summary>数量/均价来源：Flex / Gateway</summary>
    string Source,
    /// <summary>市价来源：Flex / Gateway</summary>
    string PriceSource)
{
    public static PositionPnlDto FromEntity(Position p, decimal realizedPnl) => new(
        p.Id,
        p.AccountId,
        p.Symbol,
        p.AssetType.ToString(),
        p.Currency,
        p.Quantity,
        p.AvgCost,
        p.MarketPrice,
        p.MarketValue,
        p.UnrealizedPnl,
        realizedPnl,
        p.AvgCost * p.Quantity,
        string.IsNullOrWhiteSpace(p.Source) ? "Flex" : p.Source,
        string.IsNullOrWhiteSpace(p.PriceSource) ? "Flex" : p.PriceSource);
}
