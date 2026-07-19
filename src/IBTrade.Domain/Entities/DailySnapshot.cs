using IBTrade.Domain.Common;
using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 每日账户净值快照：用于计算"每天赚多少"。
/// 净值（净清算值）= 持仓市值 + 现金；每日每个账户一条（按 账户 + 日期 唯一）。
/// 注：当前持仓数据不含现金余额，现金由快照接口显式传入（默认 0），未来接入现金/出入金报表后自动补齐。
/// </summary>
public class DailySnapshot : Entity
{
    /// <summary>账户号；"ALL" 表示跨账户汇总</summary>
    public required string AccountId { get; set; }

    /// <summary>快照日期（UTC 当日日期，不含时间）</summary>
    public DateTime DateUtc { get; set; }

    /// <summary>持仓市值合计</summary>
    public decimal TotalMarketValue { get; set; }

    /// <summary>现金余额（含未投资资金）；当前数据无此字段，由调用方传入，默认 0</summary>
    public decimal CashBalance { get; set; }

    /// <summary>账户净值（净清算值）= 持仓市值 + 现金</summary>
    public decimal NetLiquidation => TotalMarketValue + CashBalance;

    /// <summary>未实现盈亏合计</summary>
    public decimal TotalUnrealizedPnl { get; set; }

    /// <summary>已实现盈亏合计</summary>
    public decimal TotalRealizedPnl { get; set; }

    /// <summary>总盈亏 = 未实现 + 已实现</summary>
    public decimal TotalPnl { get; set; }

    /// <summary>标的数量</summary>
    public int PositionCount { get; set; }

    /// <summary>
    /// 净值来源：Flex = IB 官方 EquitySummary；Gateway = 网关盘中快照；Estimate = 持仓推算。
    /// Flex 写入后，非 Flex 来源不得覆盖同日记录。
    /// </summary>
    public string Source { get; set; } = DataSources.Flex;
}
