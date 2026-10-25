namespace IBTrade.Application.Portfolio.Dtos;

public record ClosedTradeDto(
    string AccountId,
    string Symbol,
    string AssetType,
    string Direction,
    decimal Quantity,
    decimal OpenPrice,
    decimal ClosePrice,
    decimal Multiplier,
    DateTime OpenedAtUtc,
    DateTime ClosedAtUtc,
    long OpenTradeId,
    long CloseTradeId,
    decimal GrossPnl,
    decimal Commission,
    decimal NetPnl,
    string Currency);

public record OpenLotDto(
    string AccountId,
    string Symbol,
    string AssetType,
    string Direction,
    decimal Quantity,
    decimal OpenPrice,
    decimal Multiplier,
    DateTime OpenedAtUtc,
    long TradeId,
    decimal RemainingCommission,
    string Currency);

public record FillPnlDto(long TradeId, decimal RealizedPnl);

public record TradePnlDto(
    string Scope,
    DateTime AsOfUtc,
    decimal TotalNetPnl,
    decimal GrossProfit,
    decimal GrossLoss,
    int WinCount,
    int LossCount,
    int FlatCount,
    int ClosedCount,
    int OpenLotCount,
    string Method,
    IReadOnlyList<ClosedTradeDto> Closed,
    IReadOnlyList<OpenLotDto> Open,
    IReadOnlyList<FillPnlDto> ByFill);
