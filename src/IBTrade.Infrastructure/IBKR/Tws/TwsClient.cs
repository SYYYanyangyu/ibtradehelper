using IBApi;
using IBTrade.Application.IBKR;
using IBTrade.Domain.Enums;

namespace IBTrade.Infrastructure.IBKR.Tws;

/// <summary>
/// IBKR TWS API（socket）客户端实现：连接 → 订阅账户更新 → 收齐后返回快照并断开。
/// 基于官方 IBApi 客户端（EWrapperImpl）。默认按需连接、拉完即断，由上层按节奏轮询刷新。
/// </summary>
public class TwsClient : DefaultEWrapper, ITwsClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _clientId;

    private EClientSocket? _client;
    private EReaderSignal? _signal;
    private Thread? _readerThread;

    private readonly object _lock = new();
    private readonly Dictionary<string, TwsPositionDto> _positions = new();
    private decimal _netLiquidation;
    private decimal _cashBalance;
    private string _currency = "USD";
    private string _accountId = "";

    private readonly TaskCompletionSource<string> _managedAccountsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _downloadEndTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---- 实时行情推送（持久订阅）相关字段 ----
    private readonly Dictionary<int, string> _mktReqToSymbol = new();   // 行情请求 id -> 标的代码
    private readonly Dictionary<string, Contract> _positionContracts = new(); // 标的代码 -> 合约
    private readonly Dictionary<string, decimal> _liveLast = new();     // 最新价
    private readonly Dictionary<string, decimal> _liveBid = new();      // 买价
    private readonly Dictionary<string, decimal> _liveAsk = new();      // 卖价
    private readonly Dictionary<string, decimal> _liveClose = new();    // 昨收
    private int _nextMktReqId = 1000;

    // ---- 历史 K 线（reqHistoricalData）相关字段 ----
    private int _nextHistReqId = 5000;
    private readonly Dictionary<int, List<TwsCandleDto>> _historicalBars = new();
    private readonly Dictionary<int, TaskCompletionSource<List<TwsCandleDto>>> _historicalTcs = new();
    private bool _liveMode;
    private DateTime _lastTickUtc = DateTime.MinValue;
    private volatile bool _connectClosed;

    public TwsClient(string host, int port, int clientId)
    {
        _host = host;
        _port = port;
        _clientId = clientId;
    }

    /// <summary>连接 TWS 并拉取一次账户快照（含持仓、净值、现金）。超时也返回已收集到的数据。</summary>
    public async Task<TwsSnapshot> PullSnapshotAsync(CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? dataTimeout = null)
    {
        connectTimeout ??= TimeSpan.FromSeconds(10);
        dataTimeout ??= TimeSpan.FromSeconds(15);

        try
        {
            await EnsureConnectedAsync(ct, connectTimeout.Value);
            _client!.reqAccountUpdates(true, _accountId);

            // 等待账户数据下载完成；超时则用已收到的数据
            try
            {
                await _downloadEndTcs.Task.WaitAsync(dataTimeout.Value, ct);
            }
            catch (OperationCanceledException)
            {
                // 接受超时：返回已收集数据
            }
        }
        catch (OperationCanceledException)
        {
            // 外部取消：返回已收集数据
        }
        catch
        {
            Disconnect();
            throw;
        }

        var snapshot = BuildSnapshot();
        Disconnect();
        return snapshot;
    }

    /// <summary>
    /// 拉取历史 K 线：建立连接 → reqHistoricalData → 等待 historicalDataEnd → 断开。
    /// 独立短连接，不干扰实时推送；同一 Gateway 实例的并发连接数受“Enable Multiple Clients”限制。
    /// </summary>
    public async Task<IReadOnlyList<TwsCandleDto>> GetHistoricalBarsAsync(
        string symbol, string? exchange, string? currency, string duration, string barSize,
        string? endDateTime, CancellationToken ct, TimeSpan? connectTimeout, TimeSpan? dataTimeout)
    {
        connectTimeout ??= TimeSpan.FromSeconds(10);
        dataTimeout ??= TimeSpan.FromSeconds(30);

        await EnsureConnectedAsync(ct, connectTimeout.Value);

        var reqId = _nextHistReqId++;
        var tcs = new TaskCompletionSource<List<TwsCandleDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _historicalBars[reqId] = new List<TwsCandleDto>();
            _historicalTcs[reqId] = tcs;
        }

        var contract = new Contract
        {
            Symbol = symbol,
            SecType = "STK",
            Exchange = exchange ?? "SMART",
            Currency = currency ?? "USD"
        };

        _client!.reqHistoricalData(reqId, contract, endDateTime ?? "", duration, barSize, "TRADES", 1, 1, false, new List<TagValue>());

        try
        {
            var bars = await tcs.Task.WaitAsync(dataTimeout.Value, ct);
            return bars;
        }
        finally
        {
            lock (_lock)
            {
                _historicalBars.Remove(reqId);
                _historicalTcs.Remove(reqId);
            }
            Disconnect();
        }
    }

    /// <summary>
    /// 建立并保持一个长连接，拉取账户快照后订阅全部持仓的实时行情（reqMktData），
    /// 之后连接不关闭，行情一动即更新内存中的最新价 —— 实现“价格动即刷新”。
    /// 返回建立完成时的初始快照；持续推送通过 <see cref="GetLiveSnapshot"/> 读取。
    /// </summary>
    public async Task<TwsSnapshot> StartLiveAsync(CancellationToken ct, TimeSpan? connectTimeout = null, TimeSpan? dataTimeout = null)
    {
        connectTimeout ??= TimeSpan.FromSeconds(10);
        dataTimeout ??= TimeSpan.FromSeconds(15);

        _liveMode = true;
        _connectClosed = false;
        await EnsureConnectedAsync(ct, connectTimeout.Value);
        _client!.reqAccountUpdates(true, _accountId);

        try
        {
            await _downloadEndTcs.Task.WaitAsync(dataTimeout.Value, ct);
        }
        catch (OperationCanceledException)
        {
            // 超时也继续：持仓信息可能已部分到达
        }

        SubscribeMarketData();

        var snap = BuildSnapshot();
        snap.ConnectedPort = _port;
        snap.Source = (_port is 4001 or 4002) ? "Gateway" : "TWS";
        return snap;
    }

    /// <summary>读取当前实时快照（含最新价、买卖价、按最新价重算的市值与未实现盈亏）。连接断开或从未启动时返回 null。</summary>
    public TwsLiveSnapshot? GetLiveSnapshot()
    {
        if (!_liveMode || _client is null || !_client.IsConnected())
            return null;

        lock (_lock)
        {
            var positions = _positions.Values.Select(dto =>
            {
                var last = _liveLast.TryGetValue(dto.Symbol, out var l) && l != 0 ? l : dto.MarketPrice;
                var mv = dto.Quantity * last;
                var un = (last - dto.AvgCost) * dto.Quantity;
                var change = dto.MarketPrice != 0 ? last - dto.MarketPrice : 0;
                return new TwsLivePosition
                {
                    Symbol = dto.Symbol,
                    AssetType = dto.AssetType,
                    Currency = dto.Currency,
                    Quantity = dto.Quantity,
                    AvgCost = dto.AvgCost,
                    LastPrice = last,
                    Bid = _liveBid.TryGetValue(dto.Symbol, out var b) ? b : 0,
                    Ask = _liveAsk.TryGetValue(dto.Symbol, out var a) ? a : 0,
                    MarketValue = mv,
                    UnrealizedPnl = un,
                    Change = change
                };
            }).ToList();

            return new TwsLiveSnapshot
            {
                AccountId = _accountId,
                Currency = _currency,
                NetLiquidation = _netLiquidation,
                CashBalance = _cashBalance,
                ConnectedPort = _port,
                Source = (_port is 4001 or 4002) ? "Gateway" : "TWS",
                LastTickUtc = _lastTickUtc,
                Positions = positions
            };
        }
    }

    /// <summary>停止实时推送并断开连接。</summary>
    public void StopLive()
    {
        _liveMode = false;
        try { if (_client is not null && _client.IsConnected()) _client.reqAccountUpdates(false, _accountId); } catch { }
        Disconnect();
    }

    /// <summary>连接是否已断开（供上层判断是否需自动重连）。</summary>
    public bool IsConnectionLost() => _liveMode && (_connectClosed || _client is null || !_client.IsConnected());

    /// <summary>探测 host:port 的 TCP 是否可达，用于区分“Gateway 未启动”与“已启动但未登录/未授权”。</summary>
    private static bool TcpPortReachable(string host, int port)
    {
        try
        {
            var family = host.Contains(':')
                ? System.Net.Sockets.AddressFamily.InterNetworkV6
                : System.Net.Sockets.AddressFamily.InterNetwork;
            using var s = new System.Net.Sockets.Socket(family, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            var ar = s.BeginConnect(host, port, null, null);
            return ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)) && s.Connected;
        }
        catch { return false; }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct, TimeSpan connectTimeout)
    {
        if (_client is not null && _client.IsConnected())
            return;

        _signal = new EReaderMonitorSignal();
        _client = new EClientSocket(this, _signal);

        _client.eConnect(_host, _port, _clientId);
        if (!_client.IsConnected())
        {
            string detail = TcpPortReachable(_host, _port)
                ? "端口已开放但 IB API 握手失败：请在 IB Gateway 中完成登录，并在 Settings → API 勾选 “Enable ActiveX and Socket Clients”，且 Trusted IPs 含 127.0.0.1。"
                : "端口无响应：请确认 IB Gateway 已启动并登录。";
            throw new InvalidOperationException($"无法连接 {_host}:{_port}。{detail}");
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
            catch
            {
                // 断开或线程退出时忽略
            }
        })
        {
            IsBackground = true
        };
        _readerThread.Start();

        _accountId = await _managedAccountsTcs.Task.WaitAsync(connectTimeout, ct);
    }

    private void SubscribeMarketData()
    {
        lock (_lock)
        {
            foreach (var kvp in _positionContracts)
            {
                var reqId = _nextMktReqId++;
                _mktReqToSymbol[reqId] = kvp.Key;
                _client!.reqMktData(reqId, kvp.Value, "", false, false, new List<TagValue>());
            }
        }
    }

    private TwsSnapshot BuildSnapshot()
    {
        lock (_lock)
        {
            return new TwsSnapshot
            {
                AccountId = _managedAccountsTcs.Task.Status == TaskStatus.RanToCompletion
                    ? _managedAccountsTcs.Task.Result
                    : "",
                Currency = _currency,
                NetLiquidation = _netLiquidation,
                CashBalance = _cashBalance,
                Positions = _positions.Values.ToList(),
                ConnectedPort = _port,
                Source = (_port is 4001 or 4002) ? "Gateway" : "TWS"
            };
        }
    }

    private void Disconnect()
    {
        try { _client?.eDisconnect(); } catch { }
        _client = null;
    }

    // ---------------- EWrapper 回调 ----------------

    public override void managedAccounts(string accountsList)
    {
        if (!string.IsNullOrWhiteSpace(accountsList))
            _managedAccountsTcs.TrySetResult(accountsList.Split(',')[0]);
    }

    public override void updateAccountValue(string key, string value, string currency, string accountName)
    {
        switch (key)
        {
            case "NetLiquidation":
                if (decimal.TryParse(value, out var nl))
                {
                    lock (_lock) { _netLiquidation = nl; _currency = currency; }
                }
                break;
            case "CashBalance":
            case "TotalCashBalance":
                if (decimal.TryParse(value, out var cb))
                    lock (_lock) { _cashBalance = cb; }
                break;
        }
    }

    public override void updatePortfolio(
        Contract contract, double position, double marketPrice, double marketValue,
        double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
    {
        var dto = new TwsPositionDto
        {
            Symbol = contract.Symbol,
            AssetType = MapAssetType(contract.SecType),
            Currency = contract.Currency ?? "USD",
            Quantity = (decimal)position,
            AvgCost = (decimal)averageCost,
            MarketPrice = (decimal)marketPrice,
            MarketValue = (decimal)marketValue,
            UnrealizedPnl = (decimal)unrealizedPNL,
            RealizedPnl = (decimal)realizedPNL
        };
        lock (_lock)
        {
            _positions[PositionKey(contract)] = dto;
            _positionContracts[contract.Symbol] = contract; // 供实时行情订阅复用
        }
    }

    public override void accountDownloadEnd(string account) => _downloadEndTcs.TrySetResult(true);

    public override void connectionClosed()
    {
        _connectClosed = true;
    }

    /// <summary>实时行情 tick：LAST(4)/BID(1)/ASK(2)/CLOSE(9)，更新内存最新价。</summary>
    public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
    {
        if (!_mktReqToSymbol.TryGetValue(tickerId, out var symbol))
            return;

        var p = (decimal)price;
        lock (_lock)
        {
            switch (field)
            {
                case 1: _liveBid[symbol] = p; break;
                case 2: _liveAsk[symbol] = p; break;
                case 4: _liveLast[symbol] = p; break;
                case 9: _liveClose[symbol] = p; break;
            }
            _lastTickUtc = DateTime.UtcNow;
        }
    }

    /// <summary>历史 K 线逐根回调（IBApi.Bar）。</summary>
    public override void historicalData(int reqId, Bar bar)
    {
        if (!_historicalBars.TryGetValue(reqId, out var list)) return;
        var dto = new TwsCandleDto
        {
            Time = ParseBarTime(bar.Time),
            Open = (decimal)bar.Open,
            High = (decimal)bar.High,
            Low = (decimal)bar.Low,
            Close = (decimal)bar.Close,
            Volume = (decimal)bar.Volume
        };
        lock (_lock) list.Add(dto);
    }

    /// <summary>历史 K 线接收完毕。</summary>
    public override void historicalDataEnd(int reqId, string start, string end)
    {
        if (!_historicalTcs.TryGetValue(reqId, out var tcs)) return;
        List<TwsCandleDto> bars;
        lock (_lock) bars = _historicalBars.TryGetValue(reqId, out var l) ? new List<TwsCandleDto>(l) : new();
        tcs.TrySetResult(bars);
    }

    private static DateTime ParseBarTime(string s)
    {
        if (DateTime.TryParseExact(s, "yyyyMMdd  HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dt1))
            return dt1;
        if (DateTime.TryParseExact(s, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt2))
            return dt2;
        return DateTime.TryParse(s, out var dt) ? dt : DateTime.MinValue;
    }

    public override void error(Exception e)
    {
        _managedAccountsTcs.TrySetException(e);
        _downloadEndTcs.TrySetException(e);
    }

    public override void error(int id, int errorCode, string errorMsg)
    {
        // 市场数据农场的连接/断开类信息是良性的，忽略
        if (errorCode is 2104 or 2105 or 2106 or 2107 or 2158 or 2169 or 2170) return;

        // 连接/登录类错误：标记失败，便于上层抛出明确异常
        if (errorCode is 501 or 502 or 503 or 504 or 509 or 1100 or 1101 or 1102)
        {
            var ex = new InvalidOperationException($"TWS 返回错误 {errorCode}: {errorMsg}");
            _managedAccountsTcs.TrySetException(ex);
            _downloadEndTcs.TrySetException(ex);
        }

        // 历史数据请求类错误：按 reqId 标记对应任务失败
        if (_historicalTcs.TryGetValue(id, out var htcs) && !htcs.Task.IsCompleted)
        {
            htcs.TrySetException(new InvalidOperationException($"TWS 返回错误 {errorCode}: {errorMsg}"));
        }
    }

    private static string PositionKey(Contract c) =>
        $"{c.Symbol}|{c.SecType}|{c.Currency}|{c.LastTradeDateOrContractMonth}|{c.Strike}|{c.Right}";

    private static AssetType MapAssetType(string? secType) => secType?.ToUpperInvariant() switch
    {
        "STK" => AssetType.Stock,
        "ETF" => AssetType.Etf,
        "OPT" => AssetType.Option,
        "FUT" => AssetType.Future,
        "CASH" => AssetType.Cash,
        "BOND" => AssetType.Bond,
        "FOREX" => AssetType.Forex,
        _ => AssetType.Stock
    };

    public void Dispose() => Disconnect();
}

/// <summary>创建 <see cref="TwsClient"/> 的工厂。</summary>
public class TwsClientFactory : ITwsClientFactory
{
    public ITwsClient Create(string host, int port, int clientId) => new TwsClient(host, port, clientId);
}
