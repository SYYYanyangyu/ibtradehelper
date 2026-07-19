namespace IBTrade.Application.Portfolio.Dtos;

/// <summary>单行：某账户某标的的已实现/未实现盈亏。</summary>
public record RealizedPnlRowDto(
    string AccountId, string Symbol, string AssetCategory, string? Description,
    string Currency, decimal RealizedPnl, decimal UnrealizedPnl);

/// <summary>按标的历史盈亏看板：汇总 + 逐标的明细（按已实现盈亏降序）。</summary>
public record RealizedPnlSummaryDto(
    string Scope,
    decimal TotalRealizedPnl,
    decimal TotalUnrealizedPnl,
    int SymbolCount,
    DateTime AsOfUtc,
    IReadOnlyList<RealizedPnlRowDto> Rows);
