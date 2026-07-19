using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Entities;

namespace IBTrade.Application.Orders;

/// <summary>下单服务抽象：风控校验 → 调下单客户端 → 落订单记录 → 合并实时状态。实现于基础设施层。</summary>
public interface IOrderService
{
    Task<OrderDto> PlaceAsync(PlaceOrderRequest req, CancellationToken ct = default, decimal? estimatedPrice = null);
    Task<bool> CancelAsync(string localOrderId, CancellationToken ct = default);
    OrderDto? Get(string localOrderId);
    IReadOnlyList<OrderDto> List();
    IRiskGuard Risk { get; }
}
