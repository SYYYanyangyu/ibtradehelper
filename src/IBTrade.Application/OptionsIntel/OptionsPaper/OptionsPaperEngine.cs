using System.Text.Json;
using System.Text.Json.Serialization;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.OptionsIntel.OptionsPaper;

/// <summary>
/// Paper 期权情报模拟盘：用预测市场涨跌 + 期权结构做信号，只允许 Paper。
/// 只向 Gateway Paper 发真实 OPT 合约；失败不记账。
/// </summary>
public class OptionsPaperEngine : IOptionsPaperEngine
{
    private readonly IServiceScopeFactory scopes;
    private readonly IOrderService orders;
    private readonly ILogger<OptionsPaperEngine>? logger;

    public OptionsPaperEngine(
        IServiceScopeFactory scopes,
        IOrderService orders,
        ILogger<OptionsPaperEngine>? logger = null)
    {
        this.scopes = scopes;
        this.orders = orders;
        this.logger = logger;
        LoadPersisted();
    }
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly string _path = ResolvePath();
    private OptionsPaperSettings _settings = new();
    private readonly List<OptionsPaperSignal> _signals = [];
    private readonly List<OptionsPaperFill> _fills = [];
    private decimal _netQty;
    private decimal _avgPrice;
    private decimal _realized;
    private decimal? _mark;
    private OptionsPaperSignal? _lastSignal;
    private string? _lastError;
    private DateTime? _lastTick;
    private DateTime? _lastAction;
    private bool _running;
    private bool _ticking;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<PredictionMarketDto> _markets = [];

    public bool IsRunning { get { lock (_gate) return _running; } }

    public OptionsPaperStatus GetStatus()
    {
        lock (_gate)
        {
            var unreal = _netQty != 0 && _mark is { } m ? (m - _avgPrice) * _netQty * 100m : 0m;
            return new OptionsPaperStatus
            {
                Running = _running,
                Settings = Clone(_settings),
                AccountHint = "Paper · Gateway 4002/7497 · 真实 OPT，不成交不记账",
                RiskMode = orders.Risk.Settings.Mode.ToString(),
                NetQty = _netQty,
                AvgPrice = _avgPrice,
                Mark = _mark,
                Unrealized = decimal.Round(unreal, 2),
                Realized = decimal.Round(_realized, 2),
                TotalPnl = decimal.Round(_realized + unreal, 2),
                SignalCount = _signals.Count,
                FillCount = _fills.Count,
                LastSignal = _lastSignal,
                LastError = _lastError,
                LastTickUtc = _lastTick,
                Signals = _signals.TakeLast(30).Reverse().ToList(),
                Fills = _fills.TakeLast(30).Reverse().ToList(),
                Markets = _markets
            };
        }
    }

    public void UpdateSettings(OptionsPaperSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Symbol))
            throw new ArgumentException("Symbol 必填");
        if (settings.Quantity <= 0 || settings.Quantity > 20)
            throw new ArgumentException("Paper 期权张数须在 1–20");
        if (settings.PollSeconds < 30) settings.PollSeconds = 30;
        if (settings.MinProbMove < 0.005m) settings.MinProbMove = 0.005m;
        settings.SendToGateway = true;
        settings.Symbol = OptionsIntelService.NormalizeSymbol(settings.Symbol);
        lock (_gate) _settings = settings;
        Save();
    }

    public void Start()
    {
        if (orders.Risk.Settings.Mode != TradingMode.Paper)
            throw new InvalidOperationException("期权 Paper 引擎只允许模拟盘。请把风控模式保持为 Paper。");
        CancellationToken token;
        lock (_gate)
        {
            if (_running) return;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _running = true;
        }
        _ = Task.Run(() => LoopAsync(token));
        logger?.LogInformation("Options Paper 引擎已启动，标的 {Symbol}", _settings.Symbol);
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _running = false;
        }
        cts?.Cancel();
        try { cts?.Dispose(); } catch { /* ignore */ }
        logger?.LogInformation("Options Paper 引擎已停止");
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_ticking) return;
            _ticking = true;
        }

        OptionsPaperSettings s;
        lock (_gate) s = Clone(_settings);

        if (orders.Risk.Settings.Mode != TradingMode.Paper)
        {
            lock (_gate)
            {
                _lastError = "风控不是 Paper，已跳过。";
                _ticking = false;
            }
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var prediction = sp.GetRequiredService<IPredictionMarketClient>();
            var company = sp.GetRequiredService<ICompanyFundamentalsClient>();
            var chain = sp.GetRequiredService<IOptionsChainClient>();
            var hot = await sp.GetRequiredService<IHotOptionsUniverse>().GetAsync(s.Symbol, ct);
            if (hot.Names.Count > 0 && !hot.CurrentIsHot)
            {
                lock (_gate)
                {
                    _lastError = $"{s.Symbol} 不在 Nasdaq 成交额活跃榜，已跳过。";
                    _lastSignal = new OptionsPaperSignal
                    {
                        Symbol = s.Symbol,
                        Bias = "hold",
                        Reason = $"只做热门期权标的。{s.Symbol} 不在今日成交额榜。"
                    };
                    _lastTick = DateTime.UtcNow;
                }
                return;
            }

            var predTask = prediction.SearchAsync(s.Symbol, null, ct);
            var quoteTask = company.GetAsync(s.Symbol, ct);
            var chainTask = chain.GetAsync(s.Symbol, null, ct);
            await Task.WhenAll(predTask, quoteTask, chainTask);

            var markets = (await predTask).Markets;
            var quote = (await quoteTask).Quote;
            var chainSnap = await chainTask;
            var stats = chainSnap.Stats;
            var spot = quote?.Last;
            var pcr = stats?.PutCallOiRatio;
            var atmRow = stats?.AtmStrike is { } atm
                ? chainSnap.Rows.FirstOrDefault(r => r.Strike == atm)
                : null;
            var optionMid = atmRow is null ? null : Mid(atmRow.Call);

            lock (_gate)
            {
                _markets = markets;
                _mark = optionMid ?? (_netQty != 0 ? _mark : spot);
                _lastTick = DateTime.UtcNow;
                _lastError = null;
            }

            var signal = BuildSignal(s, markets, spot, pcr);
            lock (_gate)
            {
                _lastSignal = signal;
                _signals.Add(signal);
                if (_signals.Count > 200) _signals.RemoveRange(0, _signals.Count - 200);
            }

            if (signal.Bias is "buy" or "sell")
                await ExecuteAsync(s, signal, chainSnap, ct);

            Save();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Options Paper tick 失败");
            lock (_gate) _lastError = ex.GetBaseException().Message;
        }
        finally
        {
            lock (_gate) _ticking = false;
        }
    }

    public void LoadPersisted()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var p = JsonSerializer.Deserialize<OptionsPaperPersist>(File.ReadAllText(_path), JsonOpts);
            if (p is null) return;
            lock (_gate)
            {
                _settings = p.Settings ?? new OptionsPaperSettings();
                _netQty = p.NetQty;
                _avgPrice = p.AvgPrice;
                _realized = p.Realized;
                _fills.Clear();
                _fills.AddRange(p.Fills ?? []);
            }
        }
        catch
        {
            /* 损坏则从空开始 */
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try { await TickAsync(ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger?.LogWarning(ex, "Options Paper 首次 tick 失败"); }

        while (!ct.IsCancellationRequested)
        {
            int wait;
            lock (_gate) wait = Math.Max(30, _settings.PollSeconds);
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); }
            catch (OperationCanceledException) { break; }
            if (ct.IsCancellationRequested) break;
            try { await TickAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger?.LogWarning(ex, "Options Paper 循环 tick 失败"); }
        }
    }

    private OptionsPaperSignal BuildSignal(
        OptionsPaperSettings s, IReadOnlyList<PredictionMarketDto> markets, decimal? spot, decimal? pcr)
    {
        var moved = markets
            .Where(m => m.Change is { } c && Math.Abs(c) >= s.MinProbMove)
            .OrderByDescending(m => Math.Abs(m.Change ?? 0))
            .ToList();

        if (moved.Count == 0)
        {
            var anyPrev = markets.Any(m => m.PreviousProbability is not null);
            return new OptionsPaperSignal
            {
                Symbol = s.Symbol,
                Bias = "hold",
                Spot = spot,
                PutCallOiRatio = pcr,
                Reason = anyPrev
                    ? $"预测市场涨跌未达 {s.MinProbMove:P1} 阈值，只更新盯市。"
                    : "第一次采样，只建立预测市场基线，不交易。"
            };
        }

        var top = moved[0];
        var kind = Classify(top.Question, s.Symbol);
        var chg = top.Change ?? 0;
        var bias = "hold";
        var reason = $"{top.Venue}「{top.Question}」概率 {top.PreviousProbability:P1} → {top.Probability:P1}（{chg:+0.0%;-0.0%}）";

        if (kind == "hawkish")
            bias = chg > 0 ? "sell" : "buy";
        else if (kind == "dovish")
            bias = chg > 0 ? "buy" : "sell";
        else if (kind == "bull")
            bias = chg > 0 ? "buy" : "sell";
        else if (kind == "bear")
            bias = chg > 0 ? "sell" : "buy";
        else
            reason += "；无法判定多空含义，观望。";

        if (pcr is > 1.4m && bias == "buy")
        {
            reason += $"；期权 P/C OI {pcr:0.00} 偏高，取消买入。";
            bias = "hold";
        }
        if (pcr is < 0.55m && bias == "sell")
        {
            reason += $"；期权 P/C OI {pcr:0.00} 偏低，取消卖出。";
            bias = "hold";
        }

        lock (_gate)
        {
            if (bias == "buy" && _netQty > 0)
            {
                reason += "；已有 Paper 多头，不再加仓。";
                bias = "hold";
            }
            if (bias == "sell" && _netQty <= 0)
            {
                reason += "；无 Paper 多头可平。";
                bias = "hold";
            }
            if (_lastAction is { } la && DateTime.UtcNow - la < TimeSpan.FromMinutes(20) && bias != "hold")
            {
                reason += "；20 分钟冷却中。";
                bias = "hold";
            }
        }

        return new OptionsPaperSignal
        {
            Symbol = s.Symbol,
            Bias = bias,
            Reason = reason,
            MarketQuestion = top.Question,
            Venue = top.Venue,
            Probability = top.Probability,
            Change = top.Change,
            PutCallOiRatio = pcr,
            Spot = spot
        };
    }

    private async Task ExecuteAsync(
        OptionsPaperSettings s, OptionsPaperSignal signal, OptionsChainSnapshot? chain, CancellationToken ct)
    {
        var side = signal.Bias == "buy" ? OrderSide.Buy : OrderSide.Sell;
        decimal qty;
        lock (_gate) qty = side == OrderSide.Sell ? _netQty : Math.Max(1, s.Quantity);
        if (qty <= 0) return;

        if (!s.SendToGateway)
        {
            RecordRejected(s.Symbol, side, qty, 0, "必须走 Gateway Paper 下真实期权，已禁止本地假成交。", signal.Reason);
            return;
        }

        var atm = chain?.Stats.AtmStrike;
        var expiry = chain?.SelectedExpiry;
        var row = atm is null
            ? null
            : chain!.Rows.FirstOrDefault(r => r.Strike == atm);
        if (row is null || string.IsNullOrWhiteSpace(expiry))
        {
            RecordRejected(s.Symbol, side, qty, 0, "没有官方 ATM 期权合约，拒绝下单。", signal.Reason);
            return;
        }

        var right = "C";
        var sideQuote = row.Call;
        var px = Mid(sideQuote);
        if (px is null or <= 0)
        {
            RecordRejected(s.Symbol, side, qty, 0, "ATM Call 没有官方买卖价，拒绝下单。", signal.Reason);
            return;
        }

        try
        {
            var dto = await orders.PlaceAsync(new PlaceOrderRequest
            {
                Symbol = s.Symbol,
                Side = side,
                Type = OrderType.Market,
                Quantity = qty,
                EstimatedPrice = px,
                SecType = "OPT",
                Expiry = expiry,
                Strike = row.Strike,
                Right = right,
                Multiplier = 100,
                Strategy = "options-paper-opt",
                Note = signal.Reason
            }, ct, px);

            var filled = dto.Status is OrderStatus.Filled or OrderStatus.PartiallyFilled;
            var working = dto.Status is OrderStatus.Submitted or OrderStatus.PreSubmitted
                or OrderStatus.PendingSubmit or OrderStatus.ApiPending;
            var fillPx = dto.AvgFillPrice > 0 ? dto.AvgFillPrice : px.Value;
            lock (_gate)
            {
                if (filled)
                {
                    if (side == OrderSide.Buy)
                    {
                        var cost = _avgPrice * _netQty + fillPx * qty;
                        _netQty += qty;
                        _avgPrice = _netQty == 0 ? 0 : cost / _netQty;
                    }
                    else
                    {
                        _realized += (fillPx - _avgPrice) * qty * 100m;
                        _netQty -= qty;
                        if (_netQty == 0) _avgPrice = 0;
                    }
                    _lastAction = DateTime.UtcNow;
                    _mark = fillPx;
                }
                _fills.Add(new OptionsPaperFill
                {
                    Symbol = $"{s.Symbol} {expiry} {row.Strike}C",
                    Side = side,
                    Quantity = qty,
                    Price = fillPx,
                    Venue = filled ? "gateway-paper-opt" : (working ? "gateway-working" : "rejected"),
                    BrokerOrderId = dto.BrokerOrderId?.ToString(),
                    Reason = signal.Reason,
                    Error = filled ? null : (dto.LastError ?? dto.Status.ToString())
                });
                if (_fills.Count > 200) _fills.RemoveRange(0, _fills.Count - 200);
                if (!filled)
                    _lastError = working
                        ? $"已提交 Gateway Paper（{dto.Status}），等待成交，未记账"
                        : (dto.LastError ?? $"期权单未成交：{dto.Status}");
            }
        }
        catch (Exception ex)
        {
            RecordRejected(s.Symbol, side, qty, px.Value, ex.GetBaseException().Message, signal.Reason);
            logger?.LogWarning(ex, "Gateway Paper 期权下单失败，已拒绝本地记账");
        }
    }

    private void RecordRejected(string symbol, OrderSide side, decimal qty, decimal px, string err, string reason)
    {
        lock (_gate)
        {
            _lastError = err;
            _fills.Add(new OptionsPaperFill
            {
                Symbol = symbol,
                Side = side,
                Quantity = qty,
                Price = px,
                Venue = "rejected",
                Reason = reason,
                Error = err
            });
            if (_fills.Count > 200) _fills.RemoveRange(0, _fills.Count - 200);
        }
    }

    private static decimal? Mid(OptionSideDto q)
    {
        if (q.Bid is > 0 && q.Ask is > 0) return (q.Bid.Value + q.Ask.Value) / 2m;
        if (q.Last is > 0) return q.Last;
        if (q.Bid is > 0) return q.Bid;
        if (q.Ask is > 0) return q.Ask;
        return null;
    }

    private static string Classify(string? question, string symbol)
    {
        var q = (question ?? "").ToLowerInvariant();
        if (q.Contains("hike") || q.Contains("raise") || q.Contains("higher rate") || q.Contains("加息"))
            return "hawkish";
        if (q.Contains("cut") || q.Contains("lower rate") || q.Contains("ease") || q.Contains("降息"))
            return "dovish";
        if (q.Contains("recession") || q.Contains("crash") || q.Contains("miss") || q.Contains("below"))
            return "bear";
        if (q.Contains(symbol.ToLowerInvariant()) || q.Contains("beat") || q.Contains("above") || q.Contains("earnings"))
            return "bull";
        if (q.Contains("fed") || q.Contains("fomc") || q.Contains("cpi"))
            return "hawkish";
        return "unknown";
    }

    private static OptionsPaperSettings Clone(OptionsPaperSettings s) => new()
    {
        Symbol = s.Symbol,
        Quantity = s.Quantity,
        PollSeconds = s.PollSeconds,
        MinProbMove = s.MinProbMove,
        SendToGateway = s.SendToGateway
    };

    private static string ResolvePath()
    {
        var dir = Environment.GetEnvironmentVariable("IBTRADE_DATA_DIR");
        var baseDir = string.IsNullOrWhiteSpace(dir) ? AppContext.BaseDirectory : dir!;
        return Path.Combine(baseDir, "options-paper.json");
    }

    private void Save()
    {
        try
        {
            OptionsPaperPersist p;
            lock (_gate)
            {
                p = new OptionsPaperPersist
                {
                    Settings = _settings,
                    NetQty = _netQty,
                    AvgPrice = _avgPrice,
                    Realized = _realized,
                    Fills = _fills.ToList()
                };
            }
            File.WriteAllText(_path, JsonSerializer.Serialize(p, JsonOpts));
        }
        catch { /* ignore */ }
    }

    private sealed class OptionsPaperPersist
    {
        public OptionsPaperSettings Settings { get; set; } = new();
        public decimal NetQty { get; set; }
        public decimal AvgPrice { get; set; }
        public decimal Realized { get; set; }
        public List<OptionsPaperFill> Fills { get; set; } = [];
    }
}
