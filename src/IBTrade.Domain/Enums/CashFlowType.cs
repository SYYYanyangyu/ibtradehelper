using System.Text.Json.Serialization;

namespace IBTrade.Domain.Enums;

/// <summary>外部现金流类型（不含买卖交易本身产生的现金变动）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CashFlowType
{
    /// <summary>入金</summary>
    Deposit,
    /// <summary>出金</summary>
    Withdrawal,
    /// <summary>股息</summary>
    Dividend,
    /// <summary>利息</summary>
    Interest,
    /// <summary>费用（佣金/平台费/融资利息等）</summary>
    Fee,
    /// <summary>其他</summary>
    Other
}
