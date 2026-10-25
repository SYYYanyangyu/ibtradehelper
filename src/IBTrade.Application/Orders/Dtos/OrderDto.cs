using IBTrade.Domain.Enums;

namespace IBTrade.Application.Orders.Dtos;

/// <summary>订单对外视图（含从下单客户端实时合并的成交状态）。</summary>
public class OrderDto
{
    public string Id { get; set; } = "";
    public int? BrokerOrderId { get; set; }
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal? LimitPrice { get; set; }
    public string? SecType { get; set; }
    public string? Expiry { get; set; }
    public decimal? Strike { get; set; }
    public string? Right { get; set; }
    public OrderStatus Status { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal AvgFillPrice { get; set; }
    public string? Strategy { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? LastError { get; set; }
}
