using IBTrade.Application.Orders;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.Orders;

/// <summary>
/// PostgreSQL 订单存储：实现 IOrderStore，重启后订单仍可查。
/// 单例外壳 + 每次操作开 Scoped DbContext（与引擎读持仓模式一致）。
/// </summary>
public class EfOrderStore(IServiceScopeFactory scopeFactory, ILogger<EfOrderStore>? logger = null) : IOrderStore
{
    public void Add(Order order)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(Clone(order));
        db.SaveChanges();
    }

    public void Update(Order order)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = db.Orders.FirstOrDefault(x => x.Id == order.Id);
        if (existing is null)
        {
            db.Orders.Add(Clone(order));
        }
        else
        {
            CopyOnto(order, existing);
        }
        db.SaveChanges();
    }

    public Order? Get(string id)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.Orders.AsNoTracking().FirstOrDefault(x => x.Id == id);
        return row is null ? null : Clone(row);
    }

    public IReadOnlyList<Order> List()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return db.Orders.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToList()
                .Select(Clone)
                .ToList();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "读取订单列表失败");
            throw;
        }
    }

    public IReadOnlyList<Order> ListOpen()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingSubmit
                        || o.Status == OrderStatus.PreSubmitted
                        || o.Status == OrderStatus.Submitted
                        || o.Status == OrderStatus.PartiallyFilled
                        || o.Status == OrderStatus.ApiPending)
            .OrderByDescending(x => x.CreatedAt)
            .AsEnumerable()
            .Select(Clone)
            .ToList();
    }

    private static Order Clone(Order o) => new()
    {
        Id = o.Id,
        BrokerOrderId = o.BrokerOrderId,
        Symbol = o.Symbol,
        Side = o.Side,
        Type = o.Type,
        Quantity = o.Quantity,
        LimitPrice = o.LimitPrice,
        Status = o.Status,
        FilledQuantity = o.FilledQuantity,
        AvgFillPrice = o.AvgFillPrice,
        Strategy = o.Strategy,
        Note = o.Note,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        ClosedAt = o.ClosedAt,
        LastError = o.LastError
    };

    private static void CopyOnto(Order src, Order dst)
    {
        dst.BrokerOrderId = src.BrokerOrderId;
        dst.Symbol = src.Symbol;
        dst.Side = src.Side;
        dst.Type = src.Type;
        dst.Quantity = src.Quantity;
        dst.LimitPrice = src.LimitPrice;
        dst.Status = src.Status;
        dst.FilledQuantity = src.FilledQuantity;
        dst.AvgFillPrice = src.AvgFillPrice;
        dst.Strategy = src.Strategy;
        dst.Note = src.Note;
        dst.UpdatedAt = src.UpdatedAt ?? DateTime.UtcNow;
        dst.ClosedAt = src.ClosedAt;
        dst.LastError = src.LastError;
    }
}
