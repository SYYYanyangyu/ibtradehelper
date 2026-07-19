using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Orders;

/// <summary>订单存储（内存，线程安全）。自动交易为实时会话态，重启可丢失；后续可替换为数据库仓储。</summary>
public interface IOrderStore
{
    void Add(Order order);
    void Update(Order order);
    Order? Get(string id);
    IReadOnlyList<Order> List();
    IReadOnlyList<Order> ListOpen();
}

public class InMemoryOrderStore : IOrderStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Order> _orders = new();

    public void Add(Order order)
    {
        lock (_gate) _orders[order.Id] = order;
    }

    public void Update(Order order)
    {
        lock (_gate) _orders[order.Id] = order;
    }

    public Order? Get(string id)
    {
        lock (_gate) return _orders.TryGetValue(id, out var o) ? o : null;
    }

    public IReadOnlyList<Order> List()
    {
        lock (_gate) return _orders.Values.OrderByDescending(o => o.CreatedAt).ToList();
    }

    public IReadOnlyList<Order> ListOpen()
    {
        lock (_gate)
            return _orders.Values
                .Where(o => o.Status is OrderStatus.PendingSubmit or OrderStatus.PreSubmitted
                    or OrderStatus.Submitted or OrderStatus.PartiallyFilled or OrderStatus.ApiPending)
                .ToList();
    }
}
