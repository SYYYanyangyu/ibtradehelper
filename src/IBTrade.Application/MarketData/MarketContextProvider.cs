using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.MarketData;

/// <summary>某个美东交易日的市场环境：前一交易日 VIX 收盘（FRED）与是否 FOMC 决议日（federalreserve.gov）。</summary>
public class MarketContext
{
    public decimal? PrevVix { get; set; }
    public string? PrevVixDate { get; set; }
    public bool? IsFomcDay { get; set; }
    public string? NextFomc { get; set; }
    public string? Error { get; set; }
    public DateTime? RefreshedUtc { get; set; }

    /// <summary>Gateway 的 VIX 最新值（日线最后一根，盘中即实时）。只展示，不参与开仓判断。</summary>
    public decimal? LiveVix { get; set; }
    public string? LiveVixDate { get; set; }
    public decimal? LiveVixChangePct { get; set; }
    public string? LiveVixError { get; set; }
}

/// <summary>VIX 与 FOMC 日程的进程内缓存；拉取失败时 10 分钟后重试，不编数据。</summary>
public class MarketContextProvider(IOfficialIndexQuotes fred, IMacroCalendar macro, IHistoricalDataService hist)
{
    private static readonly TimeSpan LiveTtl = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _liveLock = new(1, 1);
    private (decimal? Last, string? Date, decimal? ChgPct, string? Error) _live;
    private DateTime _liveAt;

    private async Task<(decimal? Last, string? Date, decimal? ChgPct, string? Error)> GetLiveVixAsync(CancellationToken ct)
    {
        await _liveLock.WaitAsync(ct);
        try
        {
            if (_liveAt != default && DateTime.UtcNow - _liveAt < LiveTtl) return _live;
            try
            {
                var bars = await hist.GetBarsAsync("VIX", "CBOE", "USD", "5 D", "1 day", ct, "IND", "TRADES");
                _live = bars.Count >= 2
                    ? (bars[^1].Close, bars[^1].Time.ToString("yyyy-MM-dd"),
                        bars[^2].Close > 0 ? Math.Round((bars[^1].Close / bars[^2].Close - 1m) * 100m, 2) : null, null)
                    : (null, null, null, "Gateway 没有返回 VIX");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _live = (_live.Last, _live.Date, _live.ChgPct, ex.GetBaseException().Message);
            }
            _liveAt = DateTime.UtcNow;
            return _live;
        }
        finally { _liveLock.Release(); }
    }
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<CandleDto> _vix = [];
    private string? _vixErr;
    private DateTime _vixAt;
    private IReadOnlyList<DateOnly> _fomc = [];
    private string? _fomcErr;
    private DateTime _fomcAt;

    public async Task<(IReadOnlyList<CandleDto> Bars, string? Error)> GetVixAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (Stale(_vixAt, _vixErr is null && _vix.Count > 0))
            {
                var (bars, err) = await fred.GetFredAsync("VIXCLS", ct);
                if (bars.Count > 0 || _vix.Count == 0) _vix = bars;
                _vixErr = bars.Count > 0 ? null : err ?? "FRED VIX 无数据";
                _vixAt = DateTime.UtcNow;
            }
            return (_vix, _vix.Count > 0 ? null : _vixErr);
        }
        finally { _lock.Release(); }
    }

    public async Task<(IReadOnlyList<DateOnly> Dates, string? Error)> GetFomcAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (Stale(_fomcAt, _fomcErr is null && _fomc.Count > 0))
            {
                var (dates, err) = await macro.GetFomcDecisionDatesAsync(ct);
                if (dates.Count > 0 || _fomc.Count == 0) _fomc = dates;
                _fomcErr = dates.Count > 0 ? null : err ?? "美联储日程无数据";
                _fomcAt = DateTime.UtcNow;
            }
            return (_fomc, _fomc.Count > 0 ? null : _fomcErr);
        }
        finally { _lock.Release(); }
    }

    /// <summary>严格早于 day 的最近一个 VIX 收盘。</summary>
    public static CandleDto? PrevVix(IReadOnlyList<CandleDto> vix, DateOnly day) =>
        vix.LastOrDefault(b => DateOnly.FromDateTime(b.Time) < day);

    public async Task<MarketContext> ForDayAsync(DateOnly day, CancellationToken ct)
    {
        var ctx = new MarketContext { RefreshedUtc = DateTime.UtcNow };
        var errors = new List<string>();

        var (vix, vixErr) = await GetVixAsync(ct);
        if (PrevVix(vix, day) is { } prev)
        {
            ctx.PrevVix = prev.Close;
            ctx.PrevVixDate = prev.Time.ToString("yyyy-MM-dd");
        }
        else errors.Add($"FRED VIX：{vixErr ?? "无数据"}");

        var (fomc, fomcErr) = await GetFomcAsync(ct);
        if (fomc.Count > 0)
        {
            ctx.IsFomcDay = fomc.Contains(day);
            var next = fomc.FirstOrDefault(d => d >= day);
            ctx.NextFomc = next == default ? null : next.ToString("yyyy-MM-dd");
        }
        else errors.Add($"美联储日程：{fomcErr ?? "无数据"}");

        ctx.Error = errors.Count == 0 ? null : string.Join("；", errors);

        var live = await GetLiveVixAsync(ct);
        ctx.LiveVix = live.Last;
        ctx.LiveVixDate = live.Date;
        ctx.LiveVixChangePct = live.ChgPct;
        ctx.LiveVixError = live.Error;
        return ctx;
    }

    private static bool Stale(DateTime at, bool ok) =>
        at == default || DateTime.UtcNow - at > (ok ? Ttl : RetryAfter);
}
