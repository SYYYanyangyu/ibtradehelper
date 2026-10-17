using IBApi;
using IBTrade.Application.IBKR;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Infrastructure.IBKR.Tws;

/// <summary>
/// 下单执行客户端（独立于看板实时推送）：维持一条持久连接（独立 clientId），
/// 负责 placeOrder / cancelOrder 并通过 orderStatus / execDetails 回调实时追踪订单状态。
/// 与 TwsLiveFeed（clientId=1 喂看板）是两条互不干扰的连接。
/// </summary>
public class TwsOrderClient : DefaultEWrapper, IAsyncDisposable
{
    private readonly TwsOptions _options;
    private readonly IRiskGuard _risk;
    private readonly string _host;
    private readonly int _clientId;

    private EClientSocket? _client;
    private EReaderSignal? _signal;
    private Thread? _readerThread;

    private readonly object _lock = new();
    private int _nextOrderId = -1;
    private int _connectedPort;
    private TaskCompletionSource<int> _orderIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, BrokerOrderState> _states = new();

    public TwsOrderClient(TwsOptions options, IRiskGuard risk)
    {
        _options = options;
        _risk = risk;
        _host = options.Host;
        _clientId = options.OrderClientId;
    }

    private TradingMode ActiveMode => _risk.Settings.Mode == TradingMode.Live
        ? TradingMode.Live
        : TradingMode.Paper;

    private int[] AllowedPorts => _options.PortsFor(ActiveMode);

    public bool IsConnected => _client is not null && _client.IsConnected();
    public int ConnectedPort => _connectedPort;

    // ---------------- 公共 API ----------------

    public async Task EnsureConnectedAsync(CancellationToken ct, TimeSpan timeout)
    {
        var ports = AllowedPorts;
        if (IsConnected && ports.Contains(_connectedPort)) return;
        if (IsConnected)
        {
            try { _client?.eDisconnect(); } catch { }
            _client = null;
        }

        Exception? lastError = null;
        foreach (var port in ports)
        {
            if (_orderIdTcs.Task.IsCompleted)
                _orderIdTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            $"下单客户端无法连接到 {ActiveMode} 端口（{string.Join(", ", ports)}）。Paper 只连 4002/7497，Live 只连 4001/7496。请确认对应 Gateway 已登录并开启 API。",
            lastError);
    }

    /// <summary>下限价/市价单，等待进入终态或超时后返回结果。</summary>
    public async Task<OrderResult> PlaceOrderAsync(
        PlaceOrderRequest req, CancellationToken ct, TimeSpan timeout)
    {
        await EnsureConnectedAsync(ct, timeout);
        if (ActiveMode == TradingMode.Paper && TwsOptions.IsLivePort(_connectedPort))
            throw new InvalidOperationException($"风控为 Paper，但已连实盘口 {_connectedPort}，已拒绝下单。");
        if (ActiveMode == TradingMode.Live && TwsOptions.IsPaperPort(_connectedPort))
            throw new InvalidOperationException($"风控为 Live，但已连模拟口 {_connectedPort}，已拒绝下单。");

        var symbol = req.Symbol.Trim().ToUpperInvariant();
        int id = Interlocked.Increment(ref _nextOrderId);
        var state = new BrokerOrderState
        {
            BrokerOrderId = id,
            Symbol = symbol,
            Side = req.Side,
            Quantity = req.Quantity,
            Status = OrderStatus.PendingSubmit,
            CreatedAt = DateTime.UtcNow
        };
        lock (_lock) _states[id] = state;

        var contract = BuildContract(req);
        var order = new Order
        {
            Action = req.Side == OrderSide.Buy ? "BUY" : "SELL",
            TotalQuantity = (double)req.Quantity,
            OrderType = req.Type == OrderType.Limit ? "LMT" : "MKT"
        };
        if (req.Type == OrderType.Limit && req.LimitPrice.HasValue)
            order.LmtPrice = (double)req.LimitPrice.Value;

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

    private static Contract BuildContract(PlaceOrderRequest req)
    {
        var symbol = req.Symbol.Trim().ToUpperInvariant();
        var sec = string.IsNullOrWhiteSpace(req.SecType) ? "STK" : req.SecType.Trim().ToUpperInvariant();
        if (sec is not ("STK" or "OPT"))
            throw new InvalidOperationException($"不支持的 SecType={sec}，仅 STK / OPT。");

        if (sec == "STK")
        {
            return new Contract
            {
                Symbol = symbol,
                SecType = "STK",
                Exchange = "SMART",
                Currency = "USD"
            };
        }

        if (req.Strike is null or <= 0 || string.IsNullOrWhiteSpace(req.Right) || string.IsNullOrWhiteSpace(req.Expiry))
            throw new InvalidOperationException("期权单必须提供 Expiry、Strike、Right（C/P）。");

        var right = req.Right.Trim().ToUpperInvariant();
        if (right is "CALL") right = "C";
        if (right is "PUT") right = "P";
        if (right is not ("C" or "P"))
            throw new InvalidOperationException("Right 只能是 C 或 P。");

        return new Contract
        {
            Symbol = UnderlyingFromOcc(symbol),
            SecType = "OPT",
            LastTradeDateOrContractMonth = NormalizeExpiry(req.Expiry),
            Strike = (double)req.Strike.Value,
            Right = right,
            Multiplier = req.Multiplier > 0 ? ((int)req.Multiplier).ToString() : "100",
            Exchange = "SMART",
            Currency = "USD"
        };
    }

    private static string UnderlyingFromOcc(string symbol)
    {
        if (symbol.Length >= 15 && symbol.Any(char.IsDigit))
            return symbol.Trim()[..6].Trim();
        return symbol;
    }

    private static string NormalizeExpiry(string raw)
    {
        var s = raw.Trim().Replace("-", "", StringComparison.Ordinal);
        if (s.Length >= 8 && s[..8].All(char.IsDigit)) return s[..8];
        throw new InvalidOperationException($"到期日无效：{raw}");
    }

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
