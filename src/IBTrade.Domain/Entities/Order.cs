using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>
/// 一笔订单（本地记录，独立于 IBKR 的 brokerOrderId）。
/// 用于追踪下单请求的生命周期与成交情况；状态由下单客户端回传实时更新。
/// </summary>
public class Order
{
    /// <summary>本地订单 ID（系统生成，用于 API 引用）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>IBKR 分配的订单 ID（下单成功后回填）。</summary>
    public int? BrokerOrderId { get; set; }

    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>限价单价格；市价单为 null。</summary>
    public decimal? LimitPrice { get; set; }

    /// <summary>STK / OPT；旧记录为 null（按正股处理）。</summary>
    public string? SecType { get; set; }

    /// <summary>期权到期 yyyyMMdd。</summary>
    public string? Expiry { get; set; }

    public decimal? Strike { get; set; }

    /// <summary>C / P。</summary>
    public string? Right { get; set; }

    public decimal? Multiplier { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.PendingSubmit;

    /// <summary>已成交数量。</summary>
    public decimal FilledQuantity { get; set; }

    /// <summary>成交均价。</summary>
    public decimal AvgFillPrice { get; set; }

    /// <summary>触发该订单的策略标识（手动下单可为空）。</summary>
    public string? Strategy { get; set; }

    /// <summary>备注（如自动交易标记）。</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>失败/拒绝原因。</summary>
    public string? LastError { get; set; }
}
