using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IBTrade.Application.MarketData;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.Quant.TrendScalper;

public interface ITrendScalperEngine
{
    bool IsRunning { get; }
    TrendScalperStatus GetStatus();
    void UpdateSettings(TrendScalperSettings settings);
    void Start();
    void Stop();
    /// <summary>上次进程退出时在运行则自动续跑（仍须 Paper）。</summary>
    void ResumeIfWasRunning();
    Task TickAsync(CancellationToken ct = default);
    Task FlattenAsync(CancellationToken ct = default);
}

/// <summary>
/// QQQ 0DTE 顺势短线引擎：只允许 Paper；行情与成交全部来自 IB Gateway（历史 K 线 + 期权 MIDPOINT + 真实 Paper 订单）。
/// 一次只持一笔；止盈 / 止损 / 超时 / 收盘前强平；当日连亏或笔数到上限即停。
/// </summary>
public class TrendScalperEngine : ITrendScalperEngine
{
    private const string StrategyTag = "trend-scalper";
    private static readonly TimeZoneInfo Ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeSpan Open = new(9, 30, 0);
    private static readonly TimeSpan Close = new(16, 0, 0);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IHistoricalDataService _hist;
    private readonly IOrderService _orders;
    private readonly MarketContextProvider _market;
    private readonly ILogger<TrendScalperEngine>? _logger;
    private MarketContext _ctx = new();
    private readonly object _gate = new();
    private readonly string _path = ResolvePath();

    private TrendScalperSettings _settings = new();
    private TrendScalperPosition? _position;
    private readonly List<TrendScalperTrade> _trades = [];
    private readonly List<TrendScalperSnapshot> _log = [];
    private TrendScalperDay _today = new();
    private TrendScalperSnapshot? _last;
    private string? _lastError;
    private string _session = "";
    private decimal? _lastUnderlying;
    private TimeSpan? _barOffset;
    private DateTime? _lastTick;
    private string? _lastSignalBar;
    private string? _lastExitBar;
    private bool _running;
    private bool _wasRunning;
    private bool _ticking;
    private CancellationTokenSource? _cts;

    public TrendScalperEngine(IHistoricalDataService hist, IOrderService orders, MarketContextProvider market,
        ILogger<TrendScalperEngine>? logger = null)
    {
        _hist = hist;
        _orders = orders;
        _market = market;
        _logger = logger;
        Load();
    }

    public bool IsRunning { get { lock (_gate) return _running; } }

    public TrendScalperStatus GetStatus()
    {
        lock (_gate)
        {
            return new TrendScalperStatus
            {
                Running = _running,
                Settings = Clone(_settings),
                RiskMode = _orders.Risk.Settings.Mode.ToString(),
                Session = _session,
                Position = _position,
                LastUnderlying = _lastUnderlying,
                Context = _ctx,
                Today = _today,
                Total = ComputeStats(_trades),
                Last = _last,
                LastError = _lastError,
                LastTickUtc = _lastTick,
                Trades = _trades.AsEnumerable().Reverse().Take(100).ToList(),
                Log = _log.AsEnumerable().Reverse().Take(60).ToList()
            };
        }
    }

    public void UpdateSettings(TrendScalperSettings s)
    {
        if (s.BudgetPerTrade is < 20m or > 2000m) throw new ArgumentException("单笔预算须在 $20–$2,000");
        if (s.MaxContracts is < 1 or > 10) throw new ArgumentException("张数上限须在 1–10");
        if (s.TakeProfitPct <= 0 || s.StopLossPct <= 0) throw new ArgumentException("止盈止损须为正");
        if (s.MaxHoldMinutes is < 1 or > 120) throw new ArgumentException("最长持仓须在 1–120 分钟");
        if (s.MaxTradesPerDay is < 1 or > 30) throw new ArgumentException("每日笔数须在 1–30");
        if (s.StopAfterLosses is < 1 or > 10) throw new ArgumentException("连亏停手须在 1–10");
        if (s.RangePosMin is < 0m or > 1m) throw new ArgumentException("区间位置须在 0–1");
        if (s.MaxPrevVix is < 0m or > 80m) throw new ArgumentException("VIX 上限须在 0–80（0 = 关闭）");
        if (s.DailyLossLimit is < 0m or > 10000m) throw new ArgumentException("当日亏损上限须在 $0–$10,000（0 = 关闭）");
        if (s.LimitBufferPct is < 0m or > 20m) throw new ArgumentException("限价缓冲须在 0–20%");
        var start = ParseHm(s.EntryStart, "开仓开始");
        var end = ParseHm(s.EntryEnd, "开仓结束");
        var flat = ParseHm(s.FlatBy, "强平时间");
        if (start < new TimeSpan(10, 0, 0)) throw new ArgumentException("开仓最早 10:00（需要完整的开盘区间）");
        if (end <= start || flat <= end || flat > new TimeSpan(15, 55, 0))
            throw new ArgumentException("须满足 开仓开始 < 开仓结束 < 强平时间 ≤ 15:55");
        s.Underlying = string.IsNullOrWhiteSpace(s.Underlying) ? "QQQ" : s.Underlying.Trim().ToUpperInvariant();
        if (s.PollSeconds < 10) s.PollSeconds = 10;
        lock (_gate) _settings = Clone(s);
        Save();
    }

    public void Start()
    {
        EnsurePaper();
        CancellationToken token;
        lock (_gate)
        {
            if (_running) return;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _running = true;
        }
        Save();
        _ = Task.Run(() => LoopAsync(token));
        _logger?.LogInformation("Trend Scalper 已启动（Paper）");
    }

    public void ResumeIfWasRunning()
    {
        if (!_wasRunning) return;
        try { Start(); }
        catch (InvalidOperationException ex)
        {
            lock (_gate) _lastError = $"未自动续跑：{ex.Message}";
            _logger?.LogWarning("Trend Scalper 未自动续跑：{Msg}", ex.Message);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _running = false;
        }
        cts?.Cancel();
        try { cts?.Dispose(); } catch { /* ignore */ }
        Save();
        _logger?.LogInformation("Trend Scalper 已停止");
    }

    public async Task FlattenAsync(CancellationToken ct = default)
    {
        EnsurePaper();
        TrendScalperPosition? pos;
        lock (_gate) pos = _position;
        if (pos is null) throw new InvalidOperationException("当前没有持仓");
        if (pos.PendingExitOrderId is not null) throw new InvalidOperationException("平仓单已在途，等待成交");
        var px = _lastUnderlying ?? pos.EntryUnderlying;
        await ExitAsync(Clone(_settings), pos, "manual", px, NowEt(), ct);
        Save();
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_ticking) return;
            _ticking = true;
        }
        try
        {
            await TickCoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Trend Scalper tick 失败");
            lock (_gate) _lastError = ex.GetBaseException().Message;
        }
        finally
        {
            lock (_gate)
            {
                _lastTick = DateTime.UtcNow;
                _ticking = false;
            }
            Save();
        }
    }

    private async Task TickCoreAsync(CancellationToken ct)
    {
        TrendScalperSettings s;
        lock (_gate) s = Clone(_settings);

        if (_orders.Risk.Settings.Mode != TradingMode.Paper)
        {
            lock (_gate) _lastError = "风控模式不是 Paper，已暂停。本策略只允许模拟盘。";
            return;
        }

        var nowEt = NowEt();
        RollDay(nowEt);
        var ctx = await _market.ForDayAsync(DateOnly.FromDateTime(nowEt), ct);
        lock (_gate) _ctx = ctx;
        var tod = nowEt.TimeOfDay;
        var weekend = nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        if (weekend || tod < Open || tod >= Close)
        {
            lock (_gate)
            {
                _session = weekend ? "周末休市" : tod < Open ? "盘前，等待 9:30" : "已收盘";
                if (_position is not null && !weekend && tod >= Close)
                    _lastError = $"收盘后仍有持仓 {_position.Occ}，请到 Trading 页核对 Paper 账户。";
            }
            return;
        }

        var raw = await _hist.GetBarsAsync(s.Underlying, null, "USD", "1 D", "1 min", ct);
        if (!TrendSignal.TryMapToEt(raw, nowEt, out var bars, out var offset, out var why))
        {
            lock (_gate)
            {
                _session = "行情未就绪";
                _lastError = why;
            }
            return;
        }

        var lastPx = bars[^1].Close;
        var completed = bars.Where(b => b.StartEt.AddMinutes(1) <= nowEt).ToList();
        lock (_gate)
        {
            _session = "常规时段";
            _lastUnderlying = lastPx;
            _barOffset = offset;
            _lastError = null;
        }

        TrendScalperPosition? pos;
        lock (_gate) pos = _position;
        if (pos is not null)
        {
            await ManagePositionAsync(s, pos, lastPx, nowEt, ct);
            return;
        }

        var barKey = completed.Count > 0 ? completed[^1].StartEt.ToString("HH:mm") : null;
        var gate = EntryBlockReason(s, nowEt);
        var (right, snap) = TrendSignal.Evaluate(completed, s);
        if (gate is not null)
        {
            snap.Decision = "hold";
            snap.Reason = gate + (right is null ? "" : $"（信号：{snap.Reason}）");
            Remember(snap);
            return;
        }
        Remember(snap);
        if (right is null || barKey is null) return;
        lock (_gate)
        {
            if (barKey == _lastSignalBar) return;
            if (_lastExitBar is not null && string.CompareOrdinal(barKey, _lastExitBar) <= 0) return;
            _lastSignalBar = barKey;
        }

        await EnterAsync(s, right, lastPx, snap.Reason, nowEt, offset, ct);
    }

    private string? EntryBlockReason(TrendScalperSettings s, DateTime nowEt)
    {
        var tod = nowEt.TimeOfDay;
        var start = ParseHm(s.EntryStart, "");
        var end = ParseHm(s.EntryEnd, "");
        if (tod < start || tod >= end) return $"不在开仓窗口 {s.EntryStart}–{s.EntryEnd}";
        lock (_gate)
        {
            if (s.SkipFomcDays)
            {
                if (_ctx.IsFomcDay is null) return $"美联储日程未取到，不开仓（{_ctx.Error}）";
                if (_ctx.IsFomcDay == true) return "今天是 FOMC 议息决议日，停手";
            }
            if (s.MaxPrevVix > 0)
            {
                if (_ctx.PrevVix is not { } vix) return $"前日 VIX 未取到，不开仓（{_ctx.Error}）";
                if (DateOnly.TryParse(_ctx.PrevVixDate, out var vd) && DateOnly.FromDateTime(nowEt).DayNumber - vd.DayNumber > 5)
                    return $"VIX 数据停在 {_ctx.PrevVixDate}，过旧不开仓";
                if (vix >= s.MaxPrevVix) return $"前日 VIX {vix} ≥ {s.MaxPrevVix}，高波动不追势";
            }
            if (_today.ConsecutiveLosses >= s.StopAfterLosses) return $"今日已连亏 {_today.ConsecutiveLosses} 笔，停手";
            if (_today.Trades >= s.MaxTradesPerDay) return $"今日已做 {_today.Trades} 笔，到上限";
            if (s.DailyLossLimit > 0 && EntryBudget(s) < MinEntryBudget)
                return $"今日已亏 {-_today.Realized:0.00}，距日亏上限 ${s.DailyLossLimit:0} 不足 ${MinEntryBudget:0}，停手";
        }
        return null;
    }

    private const decimal MinEntryBudget = 20m;

    /// <summary>本笔可用权利金：单笔预算与当日剩余亏损额度取小（买期权最多亏光权利金）。调用方持有 _gate。</summary>
    private decimal EntryBudget(TrendScalperSettings s) =>
        s.DailyLossLimit > 0
            ? Math.Min(s.BudgetPerTrade, s.DailyLossLimit + Math.Min(0m, _today.Realized))
            : s.BudgetPerTrade;

    private static decimal Buffer(TrendScalperSettings s, decimal mid) =>
        Math.Max(0.02m, decimal.Round(mid * s.LimitBufferPct / 100m, 2, MidpointRounding.AwayFromZero));

    private static decimal BuyLimit(TrendScalperSettings s, decimal mid) =>
        s.UseLimitOrders ? decimal.Round(mid + Buffer(s, mid), 2, MidpointRounding.AwayFromZero) : mid;

    private static decimal SellLimit(TrendScalperSettings s, decimal mid) =>
        Math.Max(0.01m, decimal.Round(mid - Buffer(s, mid), 2, MidpointRounding.AwayFromZero));

    private async Task EnterAsync(TrendScalperSettings s, string right, decimal px, string reason,
        DateTime nowEt, TimeSpan offset, CancellationToken ct)
    {
        var expiry = nowEt.ToString("yyyyMMdd");
        decimal budget;
        lock (_gate) budget = EntryBudget(s);
        string? occ = null;
        decimal strike = 0, mid = 0, limit = 0;
        var tried = new List<string>();
        for (var k = 0; k <= 8; k++)
        {
            var kStrike = right == "C" ? Math.Ceiling(px) + k : Math.Floor(px) - k;
            var kOcc = Occ(s.Underlying, nowEt, right, kStrike);
            var kMid = await OptionMidAsync(kOcc, offset, nowEt, ct);
            if (kMid is null) { tried.Add($"{kStrike}{right} 无报价"); continue; }
            if (kMid < 0.15m) { tried.Add($"{kStrike}{right} ${kMid:0.00} 太便宜"); break; }
            var kLimit = BuyLimit(s, kMid.Value);
            if (kLimit * 100m <= budget)
            {
                occ = kOcc; strike = kStrike; mid = kMid.Value; limit = kLimit;
                break;
            }
            tried.Add($"{kStrike}{right} ${kMid:0.00}");
        }
        if (occ is null)
        {
            Remember(new TrendScalperSnapshot
            {
                Price = px, Decision = "hold",
                Reason = $"有信号但没有 ≤ ${budget:0} 的 0DTE 合约：{string.Join("，", tried)}"
            });
            return;
        }

        var qty = Math.Clamp(Math.Floor(budget / (limit * 100m)), 1m, s.MaxContracts);
        var dto = await _orders.PlaceAsync(new PlaceOrderRequest
        {
            Symbol = s.Underlying,
            Side = OrderSide.Buy,
            Type = s.UseLimitOrders ? OrderType.Limit : OrderType.Market,
            Quantity = qty,
            LimitPrice = s.UseLimitOrders ? limit : null,
            EstimatedPrice = mid,
            SecType = "OPT",
            Expiry = expiry,
            Strike = strike,
            Right = right,
            Multiplier = 100,
            Strategy = StrategyTag,
            Note = reason
        }, ct, limit);
        dto = await WaitFinalAsync(dto, TimeSpan.FromSeconds(15), ct);
        if (dto.Status is not (OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected))
            dto = await CancelAndSettleAsync(dto, ct);

        if (dto.FilledQuantity > 0 || dto.Status is OrderStatus.Filled)
        {
            var filledQty = dto.FilledQuantity > 0 ? dto.FilledQuantity : qty;
            var fill = dto.AvgFillPrice > 0 ? dto.AvgFillPrice : limit;
            lock (_gate)
            {
                _position = new TrendScalperPosition
                {
                    Occ = occ, Expiry = expiry, Strike = strike, Right = right, Quantity = filledQty,
                    EntryPrice = fill, EntryMid = mid,
                    EntryUnderlying = px, OpenedAtUtc = DateTime.UtcNow,
                    EntryReason = reason, EntryOrderId = dto.Id
                };
                _today.Trades++;
            }
            Remember(new TrendScalperSnapshot
            {
                Price = px, Decision = right == "C" ? "buy-call" : "buy-put",
                Reason = $"买入 {filledQty} 张 {occ} @ {fill:0.00}（mid {mid:0.00}{(s.UseLimitOrders ? $"，限价 {limit:0.00}" : "，市价")}）；{reason}"
            });
            return;
        }

        lock (_gate) _lastError = $"开仓单未成交（{dto.Status}）{dto.LastError}";
        Remember(new TrendScalperSnapshot
        {
            Price = px, Decision = "hold",
            Reason = $"开仓单 {occ}{(s.UseLimitOrders ? $" 限价 {limit:0.00}（mid {mid:0.00}）" : "")} 15 秒内未成交（{dto.Status}），已撤单"
        });
    }

    /// <summary>撤单并等待终态；撤单途中可能已成交，以终态的成交数量为准。</summary>
    private async Task<OrderDto> CancelAndSettleAsync(OrderDto dto, CancellationToken ct)
    {
        try { await _orders.CancelAsync(dto.Id, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "撤单失败 {Id}", dto.Id);
        }
        return await WaitFinalAsync(_orders.Get(dto.Id) ?? dto, TimeSpan.FromSeconds(8), ct);
    }

    private async Task ManagePositionAsync(TrendScalperSettings s, TrendScalperPosition pos, decimal px, DateTime nowEt, CancellationToken ct)
    {
        if (pos.PendingExitOrderId is { } pendingId)
        {
            var o = _orders.Get(pendingId);
            if (o is null || o.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
            {
                lock (_gate)
                {
                    pos.PendingExitOrderId = null;
                    _lastError = $"平仓单未成交（{o?.Status.ToString() ?? "丢失"}），下一轮重试";
                }
                return;
            }
            if (o.Status is OrderStatus.Filled)
                CloseTrade(pos, o.AvgFillPrice, pos.Quantity, pos.PendingExitUnderlying ?? px, pos.PendingExitReason ?? "manual", nowEt);
            return;
        }

        var sgn = pos.Right == "C" ? 1m : -1m;
        var move = sgn * (px / pos.EntryUnderlying - 1m) * 100m;
        var held = DateTime.UtcNow - pos.OpenedAtUtc;
        string? reason = null;
        if (pos.ExitAttempts > 0) reason = pos.PendingExitReason ?? "manual";
        else if (move >= s.TakeProfitPct) reason = "tp";
        else if (move <= -s.StopLossPct) reason = "sl";
        else if (held >= TimeSpan.FromMinutes(s.MaxHoldMinutes)) reason = "time";
        else if (nowEt.TimeOfDay >= ParseHm(s.FlatBy, "")) reason = "eod";

        lock (_gate)
        {
            _last = new TrendScalperSnapshot
            {
                Price = px,
                Decision = reason is null ? "hold" : "exit",
                Reason = $"持仓 {pos.Occ}：QQQ 同向 {move:+0.000;-0.000}%（止盈 +{s.TakeProfitPct}% / 止损 −{s.StopLossPct}%），已持 {held.TotalMinutes:0.0} 分钟"
            };
        }
        if (reason is not null)
            await ExitAsync(s, pos, reason, px, nowEt, ct);
    }

    private async Task ExitAsync(TrendScalperSettings s, TrendScalperPosition pos, string reason, decimal px, DateTime nowEt, CancellationToken ct)
    {
        TimeSpan? offset;
        lock (_gate) offset = _barOffset;
        var midQ = await OptionMidAsync(pos.Occ, offset, nowEt, ct);
        lock (_gate)
        {
            pos.ExitMid ??= midQ;
            pos.PendingExitReason ??= reason;
        }
        var mid = midQ ?? pos.EntryPrice;
        // 首次用限价；未成交后、收盘前强平、手动平仓或拿不到报价时改市价，保证出得去。
        var useLimit = s.UseLimitOrders && midQ is not null && pos.ExitAttempts == 0 && reason is not ("eod" or "manual");
        var limit = SellLimit(s, mid);
        var dto = await _orders.PlaceAsync(new PlaceOrderRequest
        {
            Symbol = s.Underlying,
            Side = OrderSide.Sell,
            Type = useLimit ? OrderType.Limit : OrderType.Market,
            Quantity = pos.Quantity,
            LimitPrice = useLimit ? limit : null,
            EstimatedPrice = mid,
            SecType = "OPT",
            Expiry = pos.Expiry,
            Strike = pos.Strike,
            Right = pos.Right,
            Multiplier = 100,
            Strategy = StrategyTag,
            Note = $"exit:{reason}"
        }, ct, mid);
        lock (_gate) pos.ExitAttempts++;
        dto = await WaitFinalAsync(dto, TimeSpan.FromSeconds(15), ct);

        if (useLimit && dto.Status is not (OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected))
            dto = await CancelAndSettleAsync(dto, ct);

        if (dto.Status is OrderStatus.Filled)
        {
            CloseTrade(pos, dto.AvgFillPrice > 0 ? dto.AvgFillPrice : mid, pos.Quantity, px, reason, nowEt);
            return;
        }
        if (useLimit || dto.Status is OrderStatus.Cancelled or OrderStatus.Rejected)
        {
            if (dto.FilledQuantity > 0)
                CloseTrade(pos, dto.AvgFillPrice > 0 ? dto.AvgFillPrice : limit, dto.FilledQuantity, px, reason, nowEt);
            lock (_gate)
                _lastError = useLimit
                    ? $"限价平仓 {limit:0.00}（mid {mid:0.00}）15 秒未全部成交，已撤单，下一轮改市价"
                    : $"平仓单被拒（{dto.Status}）{dto.LastError}，下一轮重试";
            return;
        }
        lock (_gate)
        {
            pos.PendingExitOrderId = dto.Id;
            pos.PendingExitReason = reason;
            pos.PendingExitUnderlying = px;
            _lastError = $"平仓单已提交（{dto.Status}），等待成交";
        }
    }

    /// <summary>平掉 qty 张；不足全部张数时只累计到持仓上，全部平完才记一笔交易。</summary>
    private void CloseTrade(TrendScalperPosition pos, decimal exitPrice, decimal qty, decimal exitUnderlying, string reason, DateTime nowEt)
    {
        lock (_gate)
        {
            pos.ClosedQuantity += qty;
            pos.ClosedProceeds += exitPrice * qty;
            pos.Quantity -= qty;
            if (pos.Quantity > 0)
            {
                _lastError = $"已平 {pos.ClosedQuantity} 张，剩余 {pos.Quantity} 张";
                return;
            }
        }
        var totalQty = pos.ClosedQuantity;
        var avgExit = decimal.Round(pos.ClosedProceeds / totalQty, 4);
        var pnl = decimal.Round((avgExit - pos.EntryPrice) * totalQty * 100m, 2);
        decimal? slip = null;
        if (pos.EntryMid is { } em && pos.ExitMid is { } xm)
            slip = decimal.Round(((pos.EntryPrice - em) + (xm - avgExit)) * totalQty * 100m, 2);
        var trade = new TrendScalperTrade
        {
            DateEt = nowEt.ToString("yyyy-MM-dd"),
            Occ = pos.Occ, Right = pos.Right, Strike = pos.Strike, Quantity = totalQty,
            EntryAtUtc = pos.OpenedAtUtc, EntryPrice = pos.EntryPrice, EntryUnderlying = pos.EntryUnderlying,
            ExitAtUtc = DateTime.UtcNow, ExitPrice = avgExit, ExitUnderlying = exitUnderlying,
            ExitReason = reason, EntryReason = pos.EntryReason, Pnl = pnl,
            EntryMid = pos.EntryMid, ExitMid = pos.ExitMid, Slippage = slip
        };
        lock (_gate)
        {
            _trades.Add(trade);
            if (_trades.Count > 2000) _trades.RemoveRange(0, _trades.Count - 2000);
            _today.Realized += pnl;
            if (pnl > 0) { _today.Wins++; _today.ConsecutiveLosses = 0; }
            else { _today.Losses++; _today.ConsecutiveLosses++; }
            _position = null;
            _lastExitBar = nowEt.AddMinutes(-1).ToString("HH:mm");
        }
        Remember(new TrendScalperSnapshot
        {
            Price = exitUnderlying, Decision = "exit",
            Reason = $"平仓 {pos.Occ} @ {avgExit:0.00}（{ExitLabel(reason)}），盈亏 {pnl:+0.00;-0.00}" +
                     (slip is { } sl ? $"，滑点成本 {sl:0.00}" : "")
        });
    }

    private async Task<OrderDto> WaitFinalAsync(OrderDto dto, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (dto.Status is not (OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000, ct);
            dto = _orders.Get(dto.Id) ?? dto;
        }
        return dto;
    }

    /// <summary>期权最近一分钟的 MIDPOINT 收盘价；数据须是当前 15 分钟内的。</summary>
    private async Task<decimal?> OptionMidAsync(string occ, TimeSpan? offset, DateTime nowEt, CancellationToken ct)
    {
        try
        {
            var bars = await _hist.GetBarsAsync(occ, null, "USD", "1800 S", "1 min", ct, "OPT", "MIDPOINT");
            if (bars.Count == 0) return null;
            var last = bars[^1];
            if (offset is { } off && nowEt - (last.Time - off) > TimeSpan.FromMinutes(15)) return null;
            return last.Close > 0 ? last.Close : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "期权报价失败 {Occ}", occ);
            return null;
        }
    }

    private void Remember(TrendScalperSnapshot snap)
    {
        lock (_gate)
        {
            var changed = _log.Count == 0 || _log[^1].Reason != snap.Reason || _log[^1].Decision != snap.Decision;
            _last = snap;
            if (snap.Decision != "hold" || changed)
            {
                _log.Add(snap);
                if (_log.Count > 300) _log.RemoveRange(0, _log.Count - 300);
            }
        }
    }

    private void RollDay(DateTime nowEt)
    {
        var d = nowEt.ToString("yyyy-MM-dd");
        lock (_gate)
        {
            if (_today.DateEt == d) return;
            _today = new TrendScalperDay { DateEt = d };
            _lastSignalBar = null;
            _lastExitBar = null;
        }
    }

    private void EnsurePaper()
    {
        if (_orders.Risk.Settings.Mode != TradingMode.Paper)
            throw new InvalidOperationException("Trend Scalper 只允许模拟盘。请把风控模式保持为 Paper。");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await TickAsync(ct);
            int wait;
            lock (_gate)
            {
                var inSession = _session == "常规时段" || _position is not null;
                wait = inSession ? Math.Max(10, _settings.PollSeconds) : 60;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static TrendScalperStats ComputeStats(IReadOnlyList<TrendScalperTrade> trades)
    {
        var st = new TrendScalperStats { Trades = trades.Count };
        if (trades.Count == 0) return st;
        var wins = trades.Where(t => t.Pnl > 0).ToList();
        var losses = trades.Where(t => t.Pnl <= 0).ToList();
        st.Wins = wins.Count;
        st.WinRate = decimal.Round((decimal)wins.Count / trades.Count, 4);
        st.Pnl = trades.Sum(t => t.Pnl);
        st.AvgWin = wins.Count > 0 ? decimal.Round(wins.Average(t => t.Pnl), 2) : 0;
        st.AvgLoss = losses.Count > 0 ? decimal.Round(losses.Average(t => t.Pnl), 2) : 0;
        var gl = -losses.Sum(t => t.Pnl);
        st.ProfitFactor = gl > 0 ? decimal.Round(wins.Sum(t => t.Pnl) / gl, 2) : null;
        var slipped = trades.Where(t => t.Slippage is not null).ToList();
        st.SlippageTrades = slipped.Count;
        st.TotalSlippage = slipped.Sum(t => t.Slippage!.Value);
        st.AvgSlippage = slipped.Count > 0 ? decimal.Round(st.TotalSlippage / slipped.Count, 2) : null;
        return st;
    }

    private static string Occ(string root, DateTime dayEt, string right, decimal strike) =>
        $"{root}{dayEt:yyMMdd}{right}{(long)(strike * 1000m):00000000}";

    private static string ExitLabel(string r) => r switch
    {
        "tp" => "止盈",
        "sl" => "止损",
        "time" => "超时",
        "eod" => "收盘前强平",
        _ => "手动"
    };

    private static TimeSpan ParseHm(string? s, string label)
    {
        if (TimeSpan.TryParseExact(s?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t)
            || TimeSpan.TryParseExact(s?.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out t))
            return t;
        throw new ArgumentException($"{label} 格式须为 HH:mm");
    }

    private static DateTime NowEt() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ny);

    private static TrendScalperSettings Clone(TrendScalperSettings s) =>
        JsonSerializer.Deserialize<TrendScalperSettings>(JsonSerializer.Serialize(s))!;

    private static string ResolvePath()
    {
        var dir = Environment.GetEnvironmentVariable("IBTRADE_DATA_DIR");
        var baseDir = string.IsNullOrWhiteSpace(dir) ? AppContext.BaseDirectory : dir!;
        return Path.Combine(baseDir, "trend-scalper.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var p = JsonSerializer.Deserialize<Persist>(File.ReadAllText(_path), JsonOpts);
            if (p is null) return;
            lock (_gate)
            {
                _settings = p.Settings ?? new TrendScalperSettings();
                _wasRunning = p.Running;
                _position = p.Position;
                _today = p.Today ?? new TrendScalperDay();
                _trades.Clear();
                _trades.AddRange(p.Trades ?? []);
                _log.Clear();
                _log.AddRange(p.Log ?? []);
            }
        }
        catch
        {
            /* 损坏则从空开始 */
        }
    }

    private void Save()
    {
        try
        {
            Persist p;
            lock (_gate)
            {
                p = new Persist
                {
                    Running = _running,
                    Settings = _settings,
                    Position = _position,
                    Today = _today,
                    Trades = _trades.ToList(),
                    Log = _log.TakeLast(100).ToList()
                };
            }
            File.WriteAllText(_path, JsonSerializer.Serialize(p, JsonOpts));
        }
        catch { /* ignore */ }
    }

    private sealed class Persist
    {
        public bool Running { get; set; }
        public TrendScalperSettings? Settings { get; set; }
        public TrendScalperPosition? Position { get; set; }
        public TrendScalperDay? Today { get; set; }
        public List<TrendScalperTrade>? Trades { get; set; }
        public List<TrendScalperSnapshot>? Log { get; set; }
    }
}
