using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using IBTrade.Infrastructure.IBKR.Tws;
using Microsoft.Extensions.DependencyInjection;

namespace IBTrade.Infrastructure.Orders;

/// <summary>
/// 下单服务实现（基础设施层）：风控校验 → 调下单客户端 → 落订单记录 → 合并实时状态。
/// 与看板实时推送（TwsLiveFeed）共用 Gateway，但使用独立 clientId 的持久连接，互不干扰。
/// </summary>
public class OrderService : IOrderService
{
    private readonly TwsOrderClient _client;
    private readonly IOrderStore _store;
    private readonly IRiskGuard _risk;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public OrderService(TwsOrderClient client, IOrderStore store, IRiskGuard risk, IServiceScopeFactory scopeFactory)
    {
        _client = client;
        _store = store;
        _risk = risk;
        _scopeFactory = scopeFactory;
    }

    public IRiskGuard Risk => _risk;

    /// <summary>单笔数量的安全上限（防御性护栏，避免异常/恶意入参导致券商端异常）。</summary>
    private const decimal MaxSafeQuantity = 10_000_000m;

    public async Task<OrderDto> PlaceAsync(PlaceOrderRequest req, CancellationToken ct, decimal? estimatedPrice)
    {
        ValidateRequest(req);

        // 当日亏损熔断：从成交表（Flex 摄入的滚动窗口）按 FIFO 计算今日已实现盈亏。
        // 若数据库尚无任何成交记录，则数据源不可用，显式告警避免安全闸门“静默失效”。
        var dayRealizedPnl = 0m;
        if (_risk.Settings.DailyLossLimit > 0)
        {
            var trades = await GetAllTradesAsync(ct);
            dayRealizedPnl = ComputeTodayRealizedPnl(trades, DateTime.UtcNow.Date);
            if (trades.Count == 0)
                Console.WriteLine("[Risk] 警告：已配置 DailyLossLimit，但成交表为空（尚未执行 Flex 摄入），当日亏损熔断暂无法评估真实盈亏。");
        }

        if (string.Equals(req.SecType, "OPT", StringComparison.OrdinalIgnoreCase) && req.Multiplier <= 0)
            req.Multiplier = 100m;
        var est = estimatedPrice ?? req.EstimatedPrice ?? req.LimitPrice;
        var check = _risk.Check(req, _store.ListOpen(), dayRealizedPnl, est);
        if (!check.Ok)
            throw new InvalidOperationException($"风控拦截：{check.Reason}");

        if (string.Equals(req.SecType, "OPT", StringComparison.OrdinalIgnoreCase))
            ValidateOption(req);

        var result = await _client.PlaceOrderAsync(req, ct, _timeout);

        var order = new Order
        {
            Symbol = req.Symbol.Trim().ToUpperInvariant(),
            Side = req.Side,
            Type = req.Type,
            Quantity = req.Quantity,
            LimitPrice = req.LimitPrice,
            Strategy = req.Strategy,
            Note = req.Note,
            BrokerOrderId = result.BrokerOrderId,
            Status = result.Status,
            FilledQuantity = result.FilledQuantity,
            AvgFillPrice = result.AvgFillPrice,
            LastError = result.LastError,
            ClosedAt = result.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected
                ? DateTime.UtcNow : null
        };
        _store.Add(order);
        return ToDto(order);
    }

    public async Task<bool> CancelAsync(string localOrderId, CancellationToken ct)
    {
        var order = _store.Get(localOrderId);
        if (order?.BrokerOrderId is null) return false;
        var ok = await _client.CancelOrderAsync(order.BrokerOrderId.Value, ct, TimeSpan.FromSeconds(10));

        var st = _client.GetState(order.BrokerOrderId.Value);
        if (st is not null)
        {
            order.Status = st.Status;
            order.FilledQuantity = st.FilledQuantity;
            order.AvgFillPrice = st.AvgFillPrice;
            order.UpdatedAt = DateTime.UtcNow;
            if (st.Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Filled)
                order.ClosedAt = DateTime.UtcNow;
            _store.Update(order);
        }
        return ok;
    }

    public OrderDto? Get(string localOrderId)
    {
        var order = _store.Get(localOrderId);
        if (order is null) return null;
        if (MergeFromTws(order))
            _store.Update(order);
        return ToDto(order);
    }

    public IReadOnlyList<OrderDto> List()
    {
        var list = _store.List();
        var result = new List<OrderDto>(list.Count);
        foreach (var o in list)
        {
            if (MergeFromTws(o))
                _store.Update(o);
            result.Add(ToDto(o));
        }
        return result;
    }

    /// <summary>用进程内 TWS 状态刷新订单；有变化返回 true（需落库）。</summary>
    private bool MergeFromTws(Order order)
    {
        if (order.BrokerOrderId is not { } bid) return false;
        var st = _client.GetState(bid);
        if (st is null) return false;

        var changed = order.Status != st.Status
                      || order.FilledQuantity != st.FilledQuantity
                      || order.AvgFillPrice != st.AvgFillPrice;
        order.Status = st.Status;
        order.FilledQuantity = st.FilledQuantity;
        order.AvgFillPrice = st.AvgFillPrice;
        order.UpdatedAt = st.UpdatedAt;
        if (st.Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Filled)
            order.ClosedAt ??= st.ClosedAt ?? DateTime.UtcNow;
        return changed;
    }

    /// <summary>
    /// 下单前防御性校验（所有调用方：手动下单 + 自动交易引擎都会经过此处）。
    /// 拦截非法/危险入参，避免负数、零、异常数量或负限价被送达券商。
    /// </summary>
    private static void ValidateRequest(PlaceOrderRequest req)
    {
        if (req is null)
            throw new ArgumentException("下单请求不能为空");

        var symbol = (req.Symbol ?? "").Trim();
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol 必填");

        if (req.Quantity <= 0)
            throw new ArgumentException("下单数量必须为正");

        if (req.Quantity > MaxSafeQuantity)
            throw new ArgumentException($"下单数量超出安全上限（{MaxSafeQuantity:N0}）");

        if (req.Type == OrderType.Limit)
        {
            if (!req.LimitPrice.HasValue)
                throw new ArgumentException("限价单必须提供 LimitPrice");
            if (req.LimitPrice.Value <= 0)
                throw new ArgumentException("LimitPrice 必须为正");
        }
    }

    private static void ValidateOption(PlaceOrderRequest req)
    {
        if (req.Strike is null or <= 0)
            throw new ArgumentException("期权单必须提供 Strike");
        if (string.IsNullOrWhiteSpace(req.Right))
            throw new ArgumentException("期权单必须提供 Right（C/P）");
        if (string.IsNullOrWhiteSpace(req.Expiry))
            throw new ArgumentException("期权单必须提供 Expiry");
    }

    /// <summary>
    /// 读取全部成交（通过作用域解析 Scoped 仓储，避免单例直接持有 Scoped 服务）。
    /// 成交表由 Flex 摄入写入（滚动窗口）；手动下单不落成交表，故当日盈亏以最近一次摄入为准。
    /// </summary>
    private async Task<IReadOnlyList<Trade>> GetAllTradesAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        return await tradeRepo.GetAllAsync(ct);
    }

    /// <summary>
    /// 按 FIFO 计算「当日」已实现盈亏（全局，跨账户）：
    /// 以全部成交构建成本批次（买入入队），卖出按时间匹配最早批次得到已实现盈亏；
    /// 仅当该笔卖出发生在 todayUtc（UTC 当日）时才计入当日累计。
    /// 多头配对；空头等反向场景此处以多头口径近似（熔断为保守护栏，偏差方向安全）。
    /// </summary>
    private static decimal ComputeTodayRealizedPnl(IReadOnlyList<Trade> trades, DateTime todayUtc)
    {
        var lots = new Queue<(decimal Qty, decimal Price, decimal Commission)>();
        decimal realized = 0m;

        foreach (var t in trades.OrderBy(x => x.TradeTimeUtc))
        {
            if (t.Side == Side.Buy)
            {
                lots.Enqueue((t.Quantity, t.Price, t.Commission));
                continue;
            }

            // 卖出：匹配已有批次
            var sellQty = t.Quantity;
            var proceeds = t.Quantity * t.Price - t.Commission;
            decimal costBasis = 0m;

            while (sellQty > 0 && lots.Count > 0)
            {
                var lot = lots.Dequeue();
                var matched = Math.Min(sellQty, lot.Qty);
                var commissionShare = lot.Commission * (matched / lot.Qty);
                costBasis += matched * lot.Price + commissionShare;

                sellQty -= matched;
                var remainingQty = lot.Qty - matched;
                if (remainingQty > 0)
                    lots.Enqueue((remainingQty, lot.Price, lot.Commission - commissionShare));
            }

            if (t.TradeTimeUtc.Date == todayUtc)
                realized += proceeds - costBasis;
        }

        return realized;
    }

    private static OrderDto ToDto(Order o) => new()
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
        ClosedAt = o.ClosedAt,
        LastError = o.LastError
    };
}
