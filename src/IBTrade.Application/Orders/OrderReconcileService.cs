using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace IBTrade.Application.Orders;

public interface IOrderReconcileService
{
    /// <summary>
    /// 对比「本地订单成交净额」与「DB 持仓」。
    /// 不修改任何数据；持仓仍以 Flex/TWS 同步为权威。
    /// </summary>
    Task<OrderReconcileResult> ReconcileAsync(CancellationToken ct = default);
}

/// <summary>
/// 轻量对账：本地执行审计（orders）vs 券商同步持仓（positions）。
/// 差额可能来自：重启前未落库订单、外部成交、部分成交、多账户汇总差异。
/// </summary>
public class OrderReconcileService(
    IOrderStore orderStore,
    IServiceScopeFactory scopeFactory) : IOrderReconcileService
{
    private const decimal DefaultTolerance = 0.0001m;

    public async Task<OrderReconcileResult> ReconcileAsync(CancellationToken ct = default)
    {
        var orders = orderStore.List();
        var fillNet = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var openCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filledCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var o in orders)
        {
            var sym = (o.Symbol ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(sym)) continue;

            var isOpen = o.Status is OrderStatus.PendingSubmit or OrderStatus.PreSubmitted
                or OrderStatus.Submitted or OrderStatus.PartiallyFilled or OrderStatus.ApiPending;
            if (isOpen)
            {
                openCount[sym] = openCount.GetValueOrDefault(sym) + 1;
            }

            if (o.FilledQuantity <= 0) continue;
            if (o.Status is not (OrderStatus.Filled or OrderStatus.PartiallyFilled or OrderStatus.Cancelled))
                continue;

            filledCount[sym] = filledCount.GetValueOrDefault(sym) + 1;
            var signed = o.Side == OrderSide.Buy ? o.FilledQuantity : -o.FilledQuantity;
            fillNet[sym] = fillNet.GetValueOrDefault(sym) + signed;
        }

        Dictionary<string, decimal> dbPos;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var positions = await scope.ServiceProvider
                .GetRequiredService<IPositionRepository>()
                .GetAllAsync(ct);
            dbPos = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in positions)
            {
                var sym = (p.Symbol ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(sym)) continue;
                dbPos[sym] = dbPos.GetValueOrDefault(sym) + p.Quantity;
            }
        }

        var symbols = fillNet.Keys.Union(dbPos.Keys).Union(openCount.Keys)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<OrderReconcileRow>();
        foreach (var sym in symbols)
        {
            var local = fillNet.GetValueOrDefault(sym);
            var db = dbPos.GetValueOrDefault(sym);
            var delta = local - db;
            var mismatch = Math.Abs(delta) > DefaultTolerance;
            rows.Add(new OrderReconcileRow
            {
                Symbol = sym,
                LocalFillNet = Math.Round(local, 6),
                DbPositionQty = Math.Round(db, 6),
                Delta = Math.Round(delta, 6),
                OpenOrderCount = openCount.GetValueOrDefault(sym),
                FilledOrderCount = filledCount.GetValueOrDefault(sym),
                Mismatch = mismatch
            });
        }

        var mismatches = rows.Count(r => r.Mismatch);
        return new OrderReconcileResult
        {
            CheckedAt = DateTime.UtcNow,
            Tolerance = DefaultTolerance,
            MismatchCount = mismatches,
            Summary = mismatches == 0
                ? $"对账通过：{rows.Count} 个标的，本地成交净额与 DB 持仓一致（容差 {DefaultTolerance}）。"
                : $"发现 {mismatches}/{rows.Count} 个标的差额。持仓以 Flex/TWS 同步为准；请核对外部成交或历史未落库订单。",
            Rows = rows
        };
    }
}
