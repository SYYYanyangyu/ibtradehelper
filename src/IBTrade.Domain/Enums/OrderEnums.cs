using System.Text.Json.Serialization;

namespace IBTrade.Domain.Enums;

/// <summary>订单买卖方向。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderSide
{
    Buy = 0,
    Sell = 1
}

/// <summary>订单类型。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderType
{
    /// <summary>市价单：以当时可得价格立即成交（可能有滑点）。</summary>
    Market = 0,
    /// <summary>限价单：仅以指定价格或更优价格成交。</summary>
    Limit = 1
}

/// <summary>订单生命周期状态（与 IBKR orderStatus 字符串对齐）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderStatus
{
    PendingSubmit = 0,
    PreSubmitted = 1,
    Submitted = 2,
    PartiallyFilled = 3,
    Filled = 4,
    Cancelled = 5,
    Rejected = 6,
    Failed = 7,
    ApiPending = 8,
    Inactive = 9
}

/// <summary>交易模式：模拟盘（paper）或实盘（live）。实盘需显式开启。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TradingMode
{
    Paper = 0,
    Live = 1
}
