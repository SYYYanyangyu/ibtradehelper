using IBTrade.Domain.Entities;

namespace IBTrade.Application.Positions.Dtos;

/// <summary>
/// 持仓 DTO：对外暴露的数据结构，与领域实体解耦。
/// </summary>
public record PositionDto(
    long Id,
    string AccountId,
    string Symbol,
    string AssetType,
    string Currency,
    decimal Quantity,
    decimal AvgCost,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl)
{
    /// <summary>从领域实体映射为 DTO。</summary>
    public static PositionDto FromEntity(Position p) => new(
        p.Id,
        p.AccountId,
        p.Symbol,
        p.AssetType.ToString(),
        p.Currency,
        p.Quantity,
        p.AvgCost,
        p.MarketPrice,
        p.MarketValue,
        p.UnrealizedPnl);
}
