using IBTrade.Domain.Common;
using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 个股每日快照：每天每只标的一条（账户 + 日期 + 标的 唯一），用于计算"每只股票当天贡献多少盈亏"。
/// 只存快照时刻的持仓数量 / 成本价 / 市价，市值与未实现盈亏由这些派生，保证与持仓实体口径一致。
/// 当日贡献 = 今日市值 − 前一日市值（对持有股即当日盈亏的主要来源）。
/// </summary>
public class PositionDailySnapshot : Entity
{
    /// <summary>账户号；"ALL" 表示跨账户汇总</summary>
    public required string AccountId { get; set; }

    /// <summary>快照日期（UTC 当日日期，不含时间）</summary>
    public DateTime DateUtc { get; set; }

    /// <summary>标的代码，如 AAPL</summary>
    public required string Symbol { get; set; }

    /// <summary>资产类型</summary>
    public AssetType AssetType { get; set; }

    /// <summary>币种</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>持仓数量（可为负，表示做空）</summary>
    public decimal Quantity { get; set; }

    /// <summary>平均成本价</summary>
    public decimal AvgCost { get; set; }

    /// <summary>快照时刻市场价</summary>
    public decimal MarketPrice { get; set; }

    /// <summary>当日盈亏（来自 IBKR Flex MTM 段的 total，含交易/佣金/持仓变动，IBKR 官方值）。
    /// 未摄入 MTM 时为 0。个股贡献优先用此值，比"市值差分"更准（交易日数量变化时差分会失真）。</summary>
    public decimal DailyPnl { get; set; }

    /// <summary>市值 = 数量 × 市场价</summary>
    public decimal MarketValue => Quantity * MarketPrice;

    /// <summary>未实现盈亏 = (市场价 − 成本价) × 数量</summary>
    public decimal UnrealizedPnl => (MarketPrice - AvgCost) * Quantity;
}
