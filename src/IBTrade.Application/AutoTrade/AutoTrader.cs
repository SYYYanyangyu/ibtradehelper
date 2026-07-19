using System.IO;
using System.Text.Json;
using IBTrade.Application.Backtest.Strategies;
using IBTrade.Application.MarketData;
using IBTrade.Application.MarketData.Dtos;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.AutoTrade;

/// <summary>
/// 自动交易引擎（应用层实现，单例）：
///   对每个启用且开启的标的，周期拉历史 K 线 → 在已收盘 bar 上取信号 → 持仓去抖 + 同 bar 去重 →
///   演练模式只记录意图 / 真实模式过风控后下单。
///
/// 数据入口复用 IHistoricalDataService（与回测完全相同的、已验证可用的拉数路径）。
///
/// 严谨性保障：
///   1) 引擎总开关默认关闭；每个标的默认 DryRun（只发信号不下单）。
///   2) 真实下单一律走 IOrderService.PlaceAsync，复用同一套风控。
///   3) Bar-close：默认不用未收盘最后一根，消除盘中假突破。
///   4) 同 bar 信号去重：同一根已收盘 K 线的同向 Buy/Sell 只动作一次（含 DryRun）。
///   5) 持仓去抖：空仓才买入、持仓才卖出。
///   6) 串行评估 + 审计日志；市价单以信号棒收盘价估算风控金额。
/// </summary>
public class AutoTrader : IAutoTrader
{
    private readonly IHistoricalDataService _historical;
    private readonly IStrategyFactory _strategies;
    private readonly IOrderService _orders;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoTrader>? _logger;

    private readonly object _gate = new();
    private readonly Dictionary<string, AutoTradeSymbolConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AutoTradeSymbolRuntime> _runtime = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AutoTradeEvent> _events = new();
    private AutoTradeSettings _settings = new();
    private CancellationTokenSource? _cts;
    private bool _running;

    // C-8：自动交易配置/设置的持久化路径（JSON 文件，重启后保留，避免仅内存丢失）。
    private readonly string _statePath = ResolveStatePath();

    // C-1：持仓去抖辅助——避免「持久化基线 + 本会话成交」重复计数导致超卖。
    private readonly Dictionary<string, decimal> _sessionFillNet = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _prevBaseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _syncedOrderIds = new();

    /// <summary>同 bar 去重：symbol → 已动作的 (barTime, signal)。</summary>
    private readonly Dictionary<string, ActedBarSignal> _actedOnBar = new(StringComparer.OrdinalIgnoreCase);

    public AutoTrader(
        IHistoricalDataService historical,
        IStrategyFactory strategies,
        IOrderService orders,
        IServiceScopeFactory scopeFactory,
        ILogger<AutoTrader>? logger = null)
    {
        _historical = historical;
        _strategies = strategies;
        _orders = orders;
        _scopeFactory = scopeFactory;
        _logger = logger;

        // C-8：启动时从文件恢复配置/设置，避免仅内存导致重启丢失。
        LoadState();
    }

    public bool IsRunning { get { lock (_gate) return _running; } }
    public IRiskGuard Risk => _orders.Risk;
    public AutoTradeSettings Settings { get { lock (_gate) return _settings; } }

    // ---------------- 配置 / 状态 ----------------

    public IReadOnlyList<AutoTradeSymbolConfig> GetConfigs()
    {
        lock (_gate) return _configs.Values.ToList();
    }

    public void UpsertConfig(AutoTradeSymbolConfig cfg, string? operatorName = null, string? ip = null)
    {
        if (string.IsNullOrWhiteSpace(cfg.Symbol))
            throw new ArgumentException("Symbol 必填");

        if (cfg.Quantity <= 0)
            throw new ArgumentException("每笔买入数量必须为正");
        if (cfg.Quantity > 10_000_000m)
            throw new ArgumentException("每笔买入数量超出安全上限");
        if (cfg.OrderType == OrderType.Limit)
        {
            if (!cfg.LimitPrice.HasValue)
                throw new ArgumentException("限价单必须提供 LimitPrice");
            if (cfg.LimitPrice.Value <= 0)
                throw new ArgumentException("LimitPrice 必须为正");
        }

        cfg.Symbol = cfg.Symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cfg.StrategyKey)) cfg.StrategyKey = "sma_cross";
        // 策略 key 必须在白名单工厂中存在，防止脏配置进入引擎
        try { _ = _strategies.Get(cfg.StrategyKey); }
        catch (ArgumentException ex) { throw new ArgumentException(ex.Message); }
        lock (_gate) _configs[cfg.Symbol] = cfg;
        SaveState();
        Log(cfg.Symbol, AutoTradeEventType.Info, $"已更新配置：策略={cfg.StrategyKey}, DryRun={cfg.DryRun}", ip: ip, op: operatorName);
    }

    public void RemoveConfig(string symbol, string? operatorName = null, string? ip = null)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        lock (_gate) _configs.Remove(symbol);
        SaveState();
        Log(symbol, AutoTradeEventType.Info, "已移除配置", ip: ip, op: operatorName);
    }

    public void UpdateSettings(AutoTradeSettings s, string? operatorName = null, string? ip = null)
    {
        lock (_gate) _settings = s;
        SaveState();
        Log(null, AutoTradeEventType.Info,
            $"全局设置更新：Enabled={s.Enabled}, PollInterval={s.PollIntervalSeconds}s, CompletedBarsOnly={s.UseCompletedBarsOnly}",
            ip: ip, op: operatorName);
    }

    public IReadOnlyList<AutoTradeEvent> GetEvents(int max = 100)
    {
        lock (_gate) return _events.OrderByDescending(e => e.Time).Take(max).ToList();
    }

    public IReadOnlyList<AutoTradeSymbolRuntime> GetRuntime()
    {
        lock (_gate) return _runtime.Values.Select(r => new AutoTradeSymbolRuntime
        {
            Symbol = r.Symbol,
            StrategyKey = r.StrategyKey,
            LastSignal = r.LastSignal,
            SignalBarTime = r.SignalBarTime,
            UsedPenultimateBar = r.UsedPenultimateBar,
            NetQty = r.NetQty,
            OpenOrders = r.OpenOrders,
            LastEvaluatedAt = r.LastEvaluatedAt,
            LastAction = r.LastAction,
            LastError = r.LastError
        }).ToList();
    }

    // ---------------- 生命周期 ----------------

    public void Start(string? operatorName = null, string? ip = null)
    {
        lock (_gate)
        {
            if (_running) return;
            _cts = new CancellationTokenSource();
            _running = true;
        }
        var token = _cts.Token;
        _ = Task.Run(() => LoopAsync(token));
        Log(null, AutoTradeEventType.Info, "自动交易引擎已启动", ip: ip, op: operatorName);
    }

    public void Stop(string? operatorName = null, string? ip = null)
    {
        CancellationTokenSource? cts;
        lock (_gate) { cts = _cts; _running = false; }
        cts?.Cancel();
        try { cts?.Dispose(); } catch { }
        Log(null, AutoTradeEventType.Info, "自动交易引擎已停止", ip: ip, op: operatorName);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AutoTradeSettings s;
            List<AutoTradeSymbolConfig> cfgs;
            lock (_gate)
            {
                s = _settings;
                cfgs = _configs.Values.Where(c => c.Enabled).ToList();
            }

            if (s.Enabled)
            {
                foreach (var cfg in cfgs)
                {
                    if (ct.IsCancellationRequested) break;
                    try { await EvaluateAsync(cfg, ct); }
                    catch (Exception ex)
                    {
                        Log(cfg.Symbol, AutoTradeEventType.Error, $"评估异常：{ex.Message}");
                    }
                    // 标的之间留 2 秒间隔，规避 IBKR 历史数据频率限制（Pacing Violation）
                    try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                    catch { break; }
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, s.PollIntervalSeconds)), ct); }
            catch { break; }
        }
    }

    public async Task EvaluateNowAsync(string symbol, CancellationToken ct = default, string? operatorName = null, string? ip = null)
    {
        AutoTradeSymbolConfig? cfg;
        lock (_gate) _configs.TryGetValue(symbol, out cfg);
        if (cfg is null) throw new ArgumentException($"未找到标的 {symbol} 的自动交易配置");
        Log(null, AutoTradeEventType.Info, $"手动触发评估 {symbol}", ip: ip, op: operatorName);
        await EvaluateAsync(cfg, ct);
    }

    // ---------------- 核心评估 ----------------

    private async Task EvaluateAsync(AutoTradeSymbolConfig cfg, CancellationToken ct)
    {
        var sym = cfg.Symbol.Trim().ToUpperInvariant();
        var runtime = GetOrAddRuntime(sym);
        runtime.StrategyKey = cfg.StrategyKey;
        runtime.LastEvaluatedAt = DateTime.UtcNow;

        bool completedOnly;
        lock (_gate) completedOnly = _settings.UseCompletedBarsOnly;

        // 1) 拉历史 K 线
        IReadOnlyList<CandleDto> candles;
        try
        {
            candles = await _historical.GetBarsAsync(sym, null, "USD", cfg.Duration, cfg.BarSize, ct);
        }
        catch (Exception ex)
        {
            runtime.LastError = ex.Message;
            Log(sym, AutoTradeEventType.Error, $"拉取历史K线失败：{ex.Message}");
            return;
        }
        if (candles.Count < 2)
        {
            runtime.LastError = "历史K线不足";
            Log(sym, AutoTradeEventType.Error, "历史K线不足，跳过");
            return;
        }

        // 2) Bar-close：选定已收盘信号棒
        int signalIdx;
        CandleDto signalBar;
        bool usedPenultimate;
        if (completedOnly)
        {
            var resolved = BarCloseGate.ResolveSignalBar(candles, cfg.BarSize);
            if (resolved is null)
            {
                runtime.LastError = "等待 K 线收盘";
                runtime.LastAction = "未收盘 bar，跳过本轮";
                return;
            }
            (signalIdx, signalBar, usedPenultimate) = resolved.Value;
        }
        else
        {
            signalIdx = candles.Count - 1;
            signalBar = candles[^1];
            usedPenultimate = false;
        }

        // 3) 跑策略，取信号棒上的信号（非盲目取最后一根）
        IStrategy strategy;
        try { strategy = _strategies.Get(cfg.StrategyKey); }
        catch (Exception ex) { runtime.LastError = ex.Message; Log(sym, AutoTradeEventType.Error, ex.Message); return; }

        IReadOnlyList<SignalType> signals;
        try { signals = strategy.GenerateSignals(candles, cfg.Parameters); }
        catch (Exception ex) { runtime.LastError = ex.Message; Log(sym, AutoTradeEventType.Error, $"策略计算异常：{ex.Message}"); return; }

        if (signalIdx < 0 || signalIdx >= signals.Count)
        {
            runtime.LastError = "信号索引越界";
            return;
        }

        var last = signals[signalIdx];
        var prevSignal = runtime.LastSignal;
        var prevBar = runtime.SignalBarTime;
        runtime.LastSignal = last.ToString();
        runtime.SignalBarTime = signalBar.Time;
        runtime.UsedPenultimateBar = usedPenultimate;

        // 信号或 bar 变化时才写审计，避免轮询刷屏
        if (prevSignal != last.ToString() || prevBar != signalBar.Time)
        {
            var barHint = usedPenultimate ? "倒数第二根(未收盘跳过)" : "已收盘";
            Log(sym, AutoTradeEventType.Signal,
                $"策略 {cfg.StrategyKey} 信号：{last} @ {signalBar.Time:yyyy-MM-dd HH:mm}（{barHint}，共 {candles.Count} 根）",
                cfg.StrategyKey, last.ToString());
        }

        if (last is SignalType.Hold)
        {
            runtime.LastError = null;
            return;
        }

        // 4) 同 bar 去重：同一根已收盘 K 线的同向信号只动作一次（含 DryRun）
        lock (_gate)
        {
            if (_actedOnBar.TryGetValue(sym, out var acted)
                && acted.BarTime == signalBar.Time
                && string.Equals(acted.Signal, last.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                runtime.LastAction = $"已处理 {signalBar.Time:yyyy-MM-dd} {last}，跳过重复";
                runtime.LastError = null;
                return;
            }
        }

        // 5) 持仓与挂单去抖
        var (netQty, openOrders, openBuy, openSell) = await ComputePositionAsync(sym, ct);
        runtime.NetQty = netQty;
        runtime.OpenOrders = openOrders;

        OrderSide? wantSide = last switch
        {
            SignalType.Buy => (netQty <= 0 && openBuy == 0) ? OrderSide.Buy : null,
            SignalType.Sell => (netQty > 0 && openSell == 0) ? OrderSide.Sell : null,
            _ => null
        };

        if (wantSide is null)
        {
            // 目标仓位已满足：也标记该 bar 已处理，避免每轮重复刷「无动作」
            MarkActed(sym, signalBar.Time, last.ToString());
            runtime.LastError = null;
            runtime.LastAction = $"信号 {last} 但仓位已对齐，无需动作";
            return;
        }

        decimal qty = wantSide == OrderSide.Buy ? cfg.Quantity : netQty;
        if (qty <= 0)
        {
            runtime.LastError = null;
            return;
        }

        // 6) 演练模式：只记录意图，绝不发单
        if (cfg.DryRun)
        {
            runtime.LastAction = $"[演练] {signalBar.Time:yyyy-MM-dd} {last} → 将{(wantSide == OrderSide.Buy ? "买入" : "卖出")} {qty} 股";
            runtime.LastError = null;
            Log(sym, AutoTradeEventType.DryRun,
                $"[DRY-RUN] bar={signalBar.Time:yyyy-MM-dd HH:mm} 信号 {last} → 将{(wantSide == OrderSide.Buy ? "买入" : "卖出")} {qty} 股（未实际下单）",
                cfg.StrategyKey, last.ToString());
            MarkActed(sym, signalBar.Time, last.ToString());
            return;
        }

        // 7) 真实下单
        var req = new PlaceOrderRequest
        {
            Symbol = sym,
            Side = wantSide.Value,
            Type = cfg.OrderType,
            Quantity = qty,
            LimitPrice = cfg.OrderType == OrderType.Limit ? cfg.LimitPrice : null,
            Strategy = cfg.StrategyKey,
            Note = $"AUTO:{cfg.StrategyKey}:{signalBar.Time:yyyyMMddHHmm}"
        };
        decimal estPrice = cfg.OrderType == OrderType.Limit && cfg.LimitPrice.HasValue
            ? cfg.LimitPrice.Value
            : signalBar.Close;

        try
        {
            var o = await _orders.PlaceAsync(req, ct, estPrice);
            runtime.LastAction = $"{signalBar.Time:yyyy-MM-dd} {(wantSide == OrderSide.Buy ? "买入" : "卖出")} {qty:F0} → {o.Status}";
            runtime.LastError = o.LastError;
            Log(sym, AutoTradeEventType.OrderPlaced,
                $"已下单 {(wantSide == OrderSide.Buy ? "BUY" : "SELL")} {qty:F0} {sym} @估{estPrice:F2} bar={signalBar.Time:yyyy-MM-dd} → {o.Status}",
                cfg.StrategyKey, last.ToString());
            MarkActed(sym, signalBar.Time, last.ToString());
        }
        catch (Exception ex)
        {
            runtime.LastAction = $"下单被拒：{ex.Message}";
            runtime.LastError = ex.Message;
            var type = ex is InvalidOperationException ? AutoTradeEventType.RiskBlocked : AutoTradeEventType.OrderRejected;
            Log(sym, type, $"下单被拒：{ex.Message}", cfg.StrategyKey, last.ToString());
            // 被拒不标记 acted，允许下一轮在风控放宽后重试
        }
    }

    private void MarkActed(string symbol, DateTime barTime, string signal)
    {
        lock (_gate)
        {
            _actedOnBar[symbol] = new ActedBarSignal { BarTime = barTime, Signal = signal };
        }
        SaveState();
    }

    // ---------------- 辅助 ----------------

    /// <summary>
    /// 推断某标的净持仓与未完结挂单，用于去抖：
    ///   1) 基线来自持久化持仓（Flex / TWS 同步写入，重启后仍在），避免引擎重启后置零导致重复建仓；
    ///   2) 叠加本会话内「尚未被同步进 DB」的成交增量（_syncedOrderIds 之外的已成交订单）；
    ///   3) 当检测到持久化基线相对上次读取发生变化（即发生了 Flex/TWS 摄入），认定本会话成交已被 DB 吸收，
    ///      将当前已知会话成交全部标记为「已同步」并清零增量。这样 netQty = 基线（已含该笔成交），
    ///      杜绝「基线 + 会话增量」重复计数导致 SELL 信号超卖（C-1，上一轮 P0-1 修复引入的回归）。
    /// 说明：基线变化窗口内恰好到达的极新成交可能被一并标记，存在极小概率低估；该偏差方向安全
    /// （至多导致少算、不会超卖），且下一个同步周期即自愈。
    /// </summary>
    private async Task<(decimal netQty, int open, decimal openBuy, decimal openSell)> ComputePositionAsync(string symbol, CancellationToken ct)
    {
        // 1) 持久化净持仓基线（按 symbol 跨账户求和；单用户通常单账户）。
        //    仓储为 Scoped，引擎为 Singleton，故按需建作用域解析，避免捕获 Scoped 服务。
        decimal baseline = 0;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var positionRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
            var dbPositions = await positionRepo.GetAllAsync(ct);
            foreach (var p in dbPositions)
                if (string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    baseline += p.Quantity;
        }
        catch (Exception ex)
        {
            Log(symbol, AutoTradeEventType.Error, $"读取持久化持仓失败，仅按内存订单去抖：{ex.Message}");
        }

        // 2) 本会话内存订单：未完结挂单用于去抖；已成交部分推算增量（排除已被同步吸收者）。
        var orders = _orders.List();
        int open = 0;
        decimal openBuy = 0;
        decimal openSell = 0;
        decimal sessionNet = 0;
        foreach (var o in orders)
        {
            if (!string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) continue;
            bool isOpen = o.Status is OrderStatus.PendingSubmit or OrderStatus.PreSubmitted
                or OrderStatus.Submitted or OrderStatus.PartiallyFilled or OrderStatus.ApiPending;
            if (isOpen)
            {
                open++;
                if (o.Side == OrderSide.Buy) openBuy += o.Quantity; else openSell += o.Quantity;
            }
            if (o.Status is OrderStatus.Filled or OrderStatus.PartiallyFilled && !_syncedOrderIds.Contains(o.Id))
                sessionNet += o.Side == OrderSide.Buy ? o.FilledQuantity : -o.FilledQuantity;
        }

        lock (_gate)
        {
            // 基线变化 == 发生了同步：DB 已是权威，标记所有已知会话成交为「已同步」，清零增量。
            if (_prevBaseline.TryGetValue(symbol, out var prev) && prev != baseline)
            {
                foreach (var o in orders)
                    if (o.Status is OrderStatus.Filled or OrderStatus.PartiallyFilled)
                        _syncedOrderIds.Add(o.Id);
                _sessionFillNet[symbol] = 0;
            }
            else
            {
                _sessionFillNet[symbol] = sessionNet;
            }
            _prevBaseline[symbol] = baseline;
            var net = baseline + _sessionFillNet[symbol];
            return (net, open, openBuy, openSell);
        }
    }

    private AutoTradeSymbolRuntime GetOrAddRuntime(string sym)
    {
        lock (_gate)
        {
            if (!_runtime.TryGetValue(sym, out var r))
            {
                r = new AutoTradeSymbolRuntime { Symbol = sym };
                _runtime[sym] = r;
            }
            return r;
        }
    }

    private void Log(string? symbol, AutoTradeEventType type, string msg, string? strategy = null, string? signal = null, string? ip = null, string? op = null)
    {
        var ev = new AutoTradeEvent
        {
            Symbol = symbol ?? "",
            Type = type,
            Message = msg,
            StrategyKey = strategy,
            Signal = signal,
            Ip = ip,
            Operator = op
        };
        lock (_gate)
        {
            _events.Add(ev);
            while (_events.Count > 200) _events.RemoveAt(0);
        }
        _logger?.LogInformation("[AutoTrade/{Type}] {Symbol} {Msg}", type, symbol ?? "-", msg);
    }

    /// <summary>
    /// 手动操作审计入口（如下单/撤单），写入与自动交易同源的审计事件流，
    /// 便于统一追溯“谁、从哪个 IP、做了什么”。
    /// </summary>
    public void RecordAudit(AutoTradeEventType type, string message, string? ip = null, string? op = null)
        => Log(null, type, message, ip: ip, op: op);

    // ---------------- C-8：配置/设置持久化（JSON 文件）----------------

    private static string ResolveStatePath()
    {
        var dir = Environment.GetEnvironmentVariable("IBTRADE_DATA_DIR");
        var baseDir = string.IsNullOrWhiteSpace(dir) ? AppContext.BaseDirectory : dir!;
        return Path.Combine(baseDir, "autotrade.state.json");
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var json = File.ReadAllText(_statePath);
            var state = JsonSerializer.Deserialize<AutoTradeState>(json);
            if (state is null) return;
            lock (_gate)
            {
                _settings = state.Settings ?? new AutoTradeSettings();
                _configs.Clear();
                if (state.Configs is not null)
                    foreach (var c in state.Configs) _configs[c.Symbol] = c;
                _actedOnBar.Clear();
                if (state.ActedOnBar is not null)
                    foreach (var kv in state.ActedOnBar)
                        if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
                            _actedOnBar[kv.Key] = kv.Value;
            }
            _logger?.LogInformation("已加载自动交易持久化配置（{Count} 个标的）", state.Configs?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "自动交易状态文件加载失败，使用默认内存配置。");
        }
    }

    private void SaveState()
    {
        try
        {
            List<AutoTradeSymbolConfig> configs;
            AutoTradeSettings settings;
            Dictionary<string, ActedBarSignal> acted;
            lock (_gate)
            {
                configs = _configs.Values.ToList();
                settings = _settings;
                acted = _actedOnBar.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            }
            var state = new AutoTradeState { Settings = settings, Configs = configs, ActedOnBar = acted };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "自动交易状态持久化失败（仅内存生效）。");
        }
    }

    /// <summary>自动交易持久化状态载体（配置/设置 + 同 bar 去重位点）。</summary>
    private sealed class AutoTradeState
    {
        public AutoTradeSettings? Settings { get; set; }
        public List<AutoTradeSymbolConfig>? Configs { get; set; }
        public Dictionary<string, ActedBarSignal>? ActedOnBar { get; set; }
    }

    /// <summary>已动作过的 (bar, signal)，用于跨重启去重。</summary>
    private sealed class ActedBarSignal
    {
        public DateTime BarTime { get; set; }
        public string Signal { get; set; } = "";
    }
}
