using IBTrade.Domain.Common;
using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 外部现金流：入金 / 出金 / 股息 / 利息 / 费用等不含交易的现金变动。
/// 用途：从"账户净值差分"中扣除，得到纯交易盈亏（避免把大额入金算成"赚"）。
/// 金额带符号：正值 = 现金流入（入金 / 股息 / 利息），负值 = 现金流出（出金 / 费用）。
/// </summary>
public class CashTransaction : Entity
{
    /// <summary>账户号；"ALL" 表示跨账户汇总</summary>
    public required string AccountId { get; set; }

    /// <summary>发生日期（UTC 当日日期，不含时间）</summary>
    public DateTime DateUtc { get; set; }

    /// <summary>现金流类型</summary>
    public CashFlowType Type { get; set; }

    /// <summary>变动金额（带符号）：入金/股息/利息为正，出金/费用为负</summary>
    public decimal Amount { get; set; }

    /// <summary>币种</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>备注（如来源、单号）</summary>
    public string? Note { get; set; }
}
