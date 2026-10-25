using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Ingestion.Dtos;

/// <summary>解析后的账户快照（DB 无关的纯数据载体）。</summary>
public record FlexAccountDto(string AccountId, string BaseCurrency, string? Alias);

/// <summary>解析后的持仓行。</summary>
public record FlexPositionDto(
    string AccountId, string Symbol, AssetType AssetType, string Currency,
    decimal Quantity, decimal AvgCost, decimal MarketPrice, decimal Multiplier);

/// <summary>解析后的成交行。</summary>
public record FlexTradeDto(
    string AccountId, string Symbol, AssetType AssetType, Side Side, string Currency,
    decimal Quantity, decimal Price, decimal Commission, DateTime TradeTimeUtc,
    decimal Multiplier = 1m);

/// <summary>解析后的外部现金流行（入金/出金/股息等，金额带符号）。</summary>
public record FlexCashTransactionDto(
    string AccountId, CashFlowType Type, decimal Amount, string Currency, DateTime DateUtc, string? Note);

/// <summary>
/// 解析后的按标的已实现/未实现盈亏。
/// 来源：Flex "Realized and Unrealized Performance Summary" 板块的 &lt;SymbolSummary&gt;（IBKR 权威值）。
/// RealizedPnl 取自 fifoPnlRealized，UnrealizedPnl 取自 mtmPnl。
/// </summary>
public record FlexRealizedPnlDto(
    string AccountId, string Symbol, string AssetCategory, string? Description,
    string Currency, decimal RealizedPnl, decimal UnrealizedPnl);

/// <summary>
/// 解析后的逐日净值（NAV）。
/// 来源：Flex "Net Asset Value (NAV) in Base" 板块的 &lt;EquitySummaryByReportDateInBase&gt;（IBKR 权威逐日值）。
/// Total 即当日净清算值（Net Liquidation，含现金+持仓+应计利息/股息等）。
/// PositionsMv 为真实持仓市值（stock+options+bonds+commodities+notes+funds，不含现金与应计）。
/// Cash 存原始现金；入库时 CashBalance = Total - PositionsMv（现金+应计），保证 NetLiquidation=Total 精确。
/// </summary>
public record FlexDailyNavDto(
    string AccountId, DateTime DateUtc, decimal PositionsMv, decimal Cash, decimal Total);

/// <summary>
/// 解析后的逐标的逐日 MTM（市值与当日盈亏）。
/// 来源：Flex "Mark-to-Market Performance Summary in Base"（Breakout by Day）的 &lt;MTMPerformanceSummaryUnderlying&gt;。
/// CloseQuantity/ClosePrice 为当日收盘持仓与收盘价；Multiplier 期权=100、正股=1；
/// MarketValue = CloseQuantity × ClosePrice × Multiplier。Total 为 IBKR 官方当日 MTM 盈亏（含交易/佣金/持仓变动）。
/// 仅解析 symbol 与 reportDate 均非空的行（跳过 "Broker Interest"/"Total P/L" 等汇总行）。
/// </summary>
public record FlexMtmDailyDto(
    string AccountId, DateTime DateUtc, string Symbol, AssetType AssetType,
    decimal Multiplier, decimal CloseQuantity, decimal ClosePrice, decimal Total);

/// <summary>一次 Flex 报表解析的完整结果（v3 支持多账户）。</summary>
public record FlexSnapshot(
    IReadOnlyList<FlexAccountDto> Accounts,
    IReadOnlyList<FlexPositionDto> Positions,
    IReadOnlyList<FlexTradeDto> Trades,
    IReadOnlyList<FlexCashTransactionDto> CashTransactions,
    IReadOnlyList<FlexRealizedPnlDto> RealizedPnls,
    IReadOnlyList<FlexDailyNavDto> DailyNavs,
    IReadOnlyList<FlexMtmDailyDto> MtmDaily)
{
    /// <summary>现金流默认为空列表，避免调用方判空。</summary>
    public IReadOnlyList<FlexCashTransactionDto> CashTransactionsOrEmpty =>
        CashTransactions ?? Array.Empty<FlexCashTransactionDto>();

    /// <summary>已实现盈亏默认为空列表，避免调用方判空。</summary>
    public IReadOnlyList<FlexRealizedPnlDto> RealizedPnlsOrEmpty =>
        RealizedPnls ?? Array.Empty<FlexRealizedPnlDto>();

    /// <summary>逐日净值默认为空列表，避免调用方判空（未勾选 NAV 段时）。</summary>
    public IReadOnlyList<FlexDailyNavDto> DailyNavsOrEmpty =>
        DailyNavs ?? Array.Empty<FlexDailyNavDto>();

    /// <summary>逐标的逐日 MTM 默认为空列表，避免调用方判空（未勾选 MTM 段时）。</summary>
    public IReadOnlyList<FlexMtmDailyDto> MtmDailyOrEmpty =>
        MtmDaily ?? Array.Empty<FlexMtmDailyDto>();
}
