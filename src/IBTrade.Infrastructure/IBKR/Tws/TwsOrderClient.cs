using IBApi;
using IBTrade.Application.IBKR;
using IBTrade.Domain.Enums;

namespace IBTrade.Infrastructure.IBKR.Tws;

/// <summary>
/// 下单执行客户端（独立于看板实时推送）：维持一条持久连接（独立 clientId），
/// 负责 placeOrder / cancelOrder 并通过 orderStatus / execDetails 回调实时追踪订单状态。
/// 与 TwsLiveFeed（clientId=1 喂看板）是两条互不干扰的连接。
/// </summary>
public class TwsOrderClient : DefaultEWrapper, IAsyncDisposable
{
    private readonly string _host;
    private readonly int[] _ports;
    private readonly int _clientId;

    private EClientSocket? _client;
    private EReaderSignal? _signal;
    private Thread? _readerThread;

    private readonly object _lock = new();
    private int _nextOrderId = -1;
    private int _connectedPort;
    private readonly TaskCompletionSource<int> _orderIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, BrokerOrderState> _states = new();

    public TwsOrderClient(TwsOptions options)
    {
        _host = options.Host;
        _ports = options.Ports;
        _clientId = options.OrderClientId;
    }

    public bool IsConnected => _client is not null && _client.IsConnected();
    public int ConnectedPort => _connectedPort;

    // ---------------- 公共 API ----------------

    public async Task EnsureConnectedAsync(CancellationToken ct, TimeSpan timeout)
    {
        if (IsConnected) return;

        Exception? lastError = null;
        foreach (var port in _ports)
        {
            try
            {
                _signal = new EReaderMonitorSignal();
                _client = new EClientSocket(this, _signal);
                _client.eConnect(_host, port, _clientId);
                if (!_client.IsConnected())
                {
                    lastError = new InvalidOperationException($"端口 {port} 无响应");
                    _client.eDisconnect();
                    continue;
                }

                var reader = new EReader(_client, _signal);
                reader.Start();
                _readerThread = new Thread(() =>
                {
                    try
                    {
                        while (_client.IsConnected())
                        {
                            _signal.waitForSignal();
                            reader.processMsgs();
                        }
                    }
                    catch { /* 断开时忽略 */ }
                }) { IsBackground = true };
                _readerThread.Start();

                _connectedPort = port;
                _nextOrderId = await _orderIdTcs.Task.WaitAsync(timeout, ct);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                try { _client?.eDisconnect(); } catch { }
                _client = null;
            }
        }
        throw new InvalidOperationException(
            $"下单客户端无法连接到任何端口（{string.Join(", ", _ports)}）。请确认 IB Gateway 已启动、已开启 API，且启用了 “Enable Multiple Clients”（与看板推送共用同一 Gateway 需多客户端支持）。",
            lastError);
    }

    /// <summary>下限价/市价单，等待进入终态或超时后返回结果。</summary>
    public async Task<OrderResult> PlaceOrderAsync(
        string symbol, OrderSide side, OrderType type, decimal quantity, decimal? limitPrice,
        CancellationToken ct, TimeSpan timeout)
    {
        await EnsureConnectedAsync(ct, timeout);

        int id = Interlocked.Increment(ref _nextOrderId);
        var state = new BrokerOrderState
        {
            BrokerOrderId = id,
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            Status = OrderStatus.PendingSubmit,
            CreatedAt = DateTime.UtcNow
        };
        lock (_lock) _states[id] = state;

        var contract = new Contract
        {
            Symbol = symbol,
            SecType = "STK",
            Exchange = "SMART",
            Currency = "USD"
        };
        var order = new Order
        {
            Action = side == OrderSide.Buy ? "BUY" : "SELL",
            TotalQuantity = (double)quantity,
            OrderType = type == OrderType.Limit ? "LMT" : "MKT"
        };
        if (type == OrderType.Limit && limitPrice.HasValue)
            order.LmtPrice = (double)limitPrice.Value;

        _client!.placeOrder(id, contract, order);

        // 等待终态或超时
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            BrokerOrderState? st;
            lock (_lock) _states.TryGetValue(id, out st);
            if (st is not null && IsTerminal(st.Status)) break;
            await Task.Delay(120, ct);
        }

        lock (_lock) _states.TryGetValue(id, out state);
        return ToResult(state);
    }

    public async Task<bool> CancelOrderAsync(int brokerOrderId, CancellationToken ct, TimeSpan timeout)
    {
        if (!IsConnected) return false;
        _client!.cancelOrder(brokerOrderId);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            BrokerOrderState? st;
            lock (_lock) _states.TryGetValue(brokerOrderId, out st);
            if (st is not null && st.Status is OrderStatus.Cancelled or OrderStatus.Rejected) return true;
            await Task.Delay(120, ct);
        }
        // 超时仍未确认撤单状态：如实返回 false，避免前端误报「已撤销」。
        // （IB 撤单为异步，超时仅表示未能在限定时间内确认，不代表一定失败。）
        return false;
    }

    public BrokerOrderState? GetState(int brokerOrderId)
    {
        lock (_lock) return _states.TryGetValue(brokerOrderId, out var s) ? s.Clone() : null;
    }

    public IReadOnlyList<BrokerOrderState> GetStates()
    {
        lock (_lock) return _states.Values.Select(s => s.Clone()).ToList();
    }

    // ---------------- EWrapper 回调 ----------------

    public override void nextValidId(int orderId)
    {
        _nextOrderId = Math.Max(_nextOrderId, orderId);
        _orderIdTcs.TrySetResult(_nextOrderId);
    }

    public override void orderStatus(
        int orderId, string status, double filled, double remaining, double avgFillPrice,
        int permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(orderId, out var st)) return;
            st.Status = MapStatus(status);
            st.FilledQuantity = (decimal)filled;
            st.AvgFillPrice = (decimal)avgFillPrice;
            st.UpdatedAt = DateTime.UtcNow;
            if (IsTerminal(st.Status)) st.ClosedAt = DateTime.UtcNow;
        }
    }

    public override void execDetails(int reqId, Contract contract, Execution execution)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(execution.OrderId, out var st)) return;
            st.FilledQuantity = (decimal)execution.Shares;
            st.AvgFillPrice = (decimal)execution.AvgPrice;
            st.UpdatedAt = DateTime.UtcNow;
        }
    }

    public override void connectionClosed()
    {
    }

    public override void error(int id, int errorCode, string errorMsg)
    {
        // 良性市场数据/连接提示忽略
        if (errorCode is 2104 or 2105 or 2106 or 2107 or 2158 or 2169 or 2170) return;
        // 订单预处理类提示（如 TIF 被调整）属正常警告，不视为拒绝
        if (errorCode is 10349 or 103 or 104 or 105 or 106 or 107 or 108 or 109 or 110) return;

        // 仅对明确的订单拒绝类错误码标记为拒绝；其余忽略（交由 orderStatus 反映真实状态）
        bool isReject = errorCode is 201 or 202 or 203 or 321 or 404 or 405 or 406 or 407 or 458
                        || (errorCode >= 10149 && errorCode <= 10199);
        if (isReject && id > 0 && _states.ContainsKey(id))
        {
            lock (_lock)
            {
                if (_states.TryGetValue(id, out var st))
                {
                    st.Status = OrderStatus.Rejected;
                    st.LastError = $"{errorCode}: {errorMsg}";
                    st.ClosedAt = DateTime.UtcNow;
                }
            }
        }
    }

    // ---------------- 内部辅助 ----------------

    private static OrderResult ToResult(BrokerOrderState? st) => st is null
        ? new OrderResult { Status = OrderStatus.Failed, LastError = "订单状态丢失" }
        : new OrderResult
        {
            BrokerOrderId = st.BrokerOrderId,
            Status = st.Status,
            FilledQuantity = st.FilledQuantity,
            AvgFillPrice = st.AvgFillPrice,
            LastError = st.LastError
        };

    private static OrderStatus MapStatus(string s) => s switch
    {
        "PendingSubmit" => OrderStatus.PendingSubmit,
        "PreSubmitted" => OrderStatus.PreSubmitted,
        "Submitted" => OrderStatus.Submitted,
        "PartiallyFilled" => OrderStatus.PartiallyFilled,
        "Filled" => OrderStatus.Filled,
        "Cancelled" or "ApiCancelled" or "CancelledByUser" => OrderStatus.Cancelled,
        "Rejected" => OrderStatus.Rejected,
        "ApiPending" => OrderStatus.ApiPending,
        "Inactive" => OrderStatus.Inactive,
        _ => OrderStatus.Submitted
    };

    private static bool IsTerminal(OrderStatus s) =>
        s is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Failed;

    public async ValueTask DisposeAsync()
    {
        try { _client?.eDisconnect(); } catch { }
        _client = null;
        await ValueTask.CompletedTask;
    }
}

/// <summary>下单客户端内部维护的单笔订单实时状态。</summary>
public class BrokerOrderState
{
    public int BrokerOrderId { get; set; }
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal AvgFillPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? LastError { get; set; }

    public BrokerOrderState Clone() => (BrokerOrderState)MemberwiseClone();
}

/// <summary>下单结果（终态或超时快照）。</summary>
public class OrderResult
{
    public int BrokerOrderId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal AvgFillPrice { get; set; }
    public string? LastError { get; set; }
}
