using IBTrade.Application.Portfolio.Dtos;

namespace IBTrade.Application.Portfolio;

/// <summary>
/// 组合看板汇总：一次性返回总览指标 + 各标的盈亏明细，前端可直接渲染。
/// </summary>
public record PortfolioSummaryDto(
    /// <summary>账户号；当汇总全部账户时为 "ALL"</summary>
    string AccountId,
    decimal TotalMarketValue,
    decimal TotalUnrealizedPnl,
    decimal TotalRealizedPnl,
    decimal TotalCostBasis,
    /// <summary>总盈亏 = 未实现 + 已实现</summary>
    decimal TotalPnl,
    int PositionCount,
    DateTime AsOfUtc,
    IReadOnlyList<PositionPnlDto> Positions);
