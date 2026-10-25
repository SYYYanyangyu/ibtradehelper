using System.Globalization;
using System.Text.RegularExpressions;
using IBTrade.Application.IBKR;

namespace IBTrade.Application.MarketData;

public class MarketNewsItem
{
    public DateTime? TimeUtc { get; set; }
    public string RawTime { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ArticleId { get; set; } = "";
    public string Headline { get; set; } = "";
    /// <summary>IBKR 头条元数据里的情绪分 K（−1 利空 … +1 利多），没有就是 null。</summary>
    public decimal? Sentiment { get; set; }
    public decimal? Confidence { get; set; }
    public List<string> Tags { get; set; } = [];
}

public class MarketNewsSummary
{
    public int WindowMinutes { get; set; }
    public int Count { get; set; }
    public int Scored { get; set; }
    public decimal? AvgSentiment { get; set; }
    public int Fed { get; set; }
    public int Geo { get; set; }
    public int Macro { get; set; }
}

public class MarketNewsTapeStatus
{
    public bool Connected { get; set; }
    public DateTime? SinceUtc { get; set; }
    public DateTime? LastHeadlineUtc { get; set; }
    public int Received { get; set; }
    public string? Error { get; set; }
    public List<string> ProviderErrors { get; set; } = [];
}

public class MarketNewsFeed
{
    public string Symbol { get; set; } = "";
    public List<string> Providers { get; set; } = [];
    public DateTime? RefreshedUtc { get; set; }
    public string? Error { get; set; }
    /// <summary>全市场快讯 + 本标的头条合并后的近 2 小时统计。</summary>
    public MarketNewsSummary Summary { get; set; } = new();
    /// <summary>本标的头条（reqHistoricalNews）。</summary>
    public List<MarketNewsItem> Items { get; set; } = [];
    /// <summary>全市场快讯（常驻订阅，API 启动后才开始累积）。</summary>
    public List<MarketNewsItem> Tape { get; set; } = [];
    public MarketNewsTapeStatus TapeStatus { get; set; } = new();
}

/// <summary>
/// IBKR API 新闻（账号已开通的新闻源）。只做展示和标签，不参与下单判断：
/// 历史新闻无法回测，未经验证的信号不进入规则。
/// 标签按关键词匹配；情绪分取 IBKR 头条自带的 K 值，没有就不显示。
/// </summary>
public class MarketNewsService(IGatewayNewsSource gateway)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ProvidersTtl = TimeSpan.FromHours(6);
    private const int SummaryWindowMinutes = 120;

    private static readonly Dictionary<string, Regex> TagRules = new()
    {
        ["美联储"] = Kw(@"fed|fomc|powell|rate cuts?|rate hikes?|interest rates?|treasury yields?|bond yields?|central bank|waller|bowman|jefferson|goolsbee|kashkari|logan|hammack|musalem|daly|barkin|bostic"),
        ["地缘"] = Kw(@"war|wars|iran|israel|gaza|hamas|hezbollah|russia|ukraine|taiwan|missiles?|airstrikes?|military|sanctions?|tariffs?|geopolitic\w*|conflict|ceasefire|opec|north korea|red sea|strait"),
        ["数据"] = Kw(@"cpi|ppi|pce|inflation|payrolls?|nonfarm|jobs report|jobless claims|unemployment|gdp|retail sales|ism|consumer confidence|consumer sentiment"),
        ["财报"] = Kw(@"earnings|guidance|revenue|eps|quarterly results|beats?|misses?")
    };

    private static readonly Regex Meta = new(@"^\{(?<meta>[^}]*)\}(?<text>.*)$", RegexOptions.Singleline);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, MarketNewsFeed> _cache = new(StringComparer.OrdinalIgnoreCase);
    private List<TwsNewsProviderDto> _providers = [];
    private DateTime _providersAt;

    private const int TapeCapacity = 400;
    private readonly object _tapeLock = new();
    private readonly LinkedList<MarketNewsItem> _tape = new();
    private readonly HashSet<string> _tapeIds = [];
    private readonly MarketNewsTapeStatus _tapeStatus = new();

    public async Task<IReadOnlyList<string>> GetProviderCodesAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_providers.Count == 0 || DateTime.UtcNow - _providersAt > ProvidersTtl)
            {
                _providers = (await gateway.GetNewsProvidersAsync(ct)).ToList();
                _providersAt = DateTime.UtcNow;
            }
            return _providers.Select(p => p.Code).ToList();
        }
        finally { _lock.Release(); }
    }

    public void Ingest(TwsNewsHeadlineDto h)
    {
        var item = Parse(h);
        lock (_tapeLock)
        {
            if (!_tapeIds.Add($"{item.Provider}|{item.ArticleId}")) return;
            _tape.AddFirst(item);
            while (_tape.Count > TapeCapacity)
            {
                var last = _tape.Last!.Value;
                _tapeIds.Remove($"{last.Provider}|{last.ArticleId}");
                _tape.RemoveLast();
            }
            _tapeStatus.Received++;
            _tapeStatus.LastHeadlineUtc = item.TimeUtc ?? DateTime.UtcNow;
        }
    }

    public void SetTapeState(bool connected, string? error)
    {
        lock (_tapeLock)
        {
            if (connected && !_tapeStatus.Connected)
            {
                _tapeStatus.SinceUtc = DateTime.UtcNow;
                _tapeStatus.ProviderErrors.Clear();
            }
            _tapeStatus.Connected = connected;
            _tapeStatus.Error = error;
        }
    }

    public void AddTapeProviderError(string message)
    {
        lock (_tapeLock)
        {
            if (_tapeStatus.ProviderErrors.Count < 20) _tapeStatus.ProviderErrors.Add(message);
        }
    }

    public async Task<MarketNewsFeed> GetAsync(string symbol, CancellationToken ct)
    {
        var feed = await GetSymbolFeedAsync(symbol, ct);
        List<MarketNewsItem> tape;
        MarketNewsTapeStatus status;
        lock (_tapeLock)
        {
            tape = _tape.Take(100).ToList();
            status = new MarketNewsTapeStatus
            {
                Connected = _tapeStatus.Connected,
                SinceUtc = _tapeStatus.SinceUtc,
                LastHeadlineUtc = _tapeStatus.LastHeadlineUtc,
                Received = _tapeStatus.Received,
                Error = _tapeStatus.Error,
                ProviderErrors = [.. _tapeStatus.ProviderErrors]
            };
        }
        var merged = tape.Concat(feed.Items)
            .GroupBy(i => $"{i.Provider}|{i.ArticleId}").Select(g => g.First()).ToList();
        return new MarketNewsFeed
        {
            Symbol = feed.Symbol,
            Providers = feed.Providers,
            RefreshedUtc = feed.RefreshedUtc,
            Error = feed.Error,
            Items = feed.Items,
            Tape = tape,
            TapeStatus = status,
            Summary = Summarize(merged)
        };
    }

    private async Task<MarketNewsFeed> GetSymbolFeedAsync(string symbol, CancellationToken ct)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(symbol, out var hit) && hit.RefreshedUtc is { } at && DateTime.UtcNow - at < Ttl)
                return hit;

            var feed = new MarketNewsFeed { Symbol = symbol, RefreshedUtc = DateTime.UtcNow };
            try
            {
                if (_providers.Count == 0 || DateTime.UtcNow - _providersAt > ProvidersTtl)
                {
                    _providers = (await gateway.GetNewsProvidersAsync(ct)).ToList();
                    _providersAt = DateTime.UtcNow;
                }
                feed.Providers = _providers.Select(p => $"{p.Code} · {p.Name}").ToList();
                if (_providers.Count == 0)
                {
                    feed.Error = "这个 IBKR 账号没有开通任何 API 新闻源";
                }
                else
                {
                    var raw = await gateway.GetHeadlinesAsync(symbol, string.Join("+", _providers.Select(p => p.Code)), 60, ct);
                    feed.Items = raw.Select(Parse).OrderByDescending(i => i.TimeUtc ?? DateTime.MinValue).ToList();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                feed.Error = ex.GetBaseException().Message;
                if (hit is not null) feed.Items = hit.Items;
            }

            _cache[symbol] = feed;
            return feed;
        }
        finally { _lock.Release(); }
    }

    private static MarketNewsItem Parse(TwsNewsHeadlineDto h)
    {
        var item = new MarketNewsItem
        {
            RawTime = h.RawTime,
            Provider = h.Provider,
            ArticleId = h.ArticleId,
            Headline = h.Headline.Trim()
        };
        var m = Meta.Match(item.Headline);
        if (m.Success)
        {
            item.Headline = m.Groups["text"].Value.Trim();
            var parts = m.Groups["meta"].Value.Split(':');
            for (var i = 0; i + 1 < parts.Length; i += 2)
            {
                if (!decimal.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (parts[i] == "K") item.Sentiment = v;
                else if (parts[i] == "C") item.Confidence = v;
            }
        }
        if (DateTime.TryParse(h.RawTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
            item.TimeUtc = DateTime.SpecifyKind(t, DateTimeKind.Utc);
        item.Tags = TagRules.Where(r => r.Value.IsMatch(item.Headline)).Select(r => r.Key).ToList();
        return item;
    }

    private static MarketNewsSummary Summarize(List<MarketNewsItem> items)
    {
        var since = DateTime.UtcNow.AddMinutes(-SummaryWindowMinutes);
        var recent = items.Where(i => i.TimeUtc >= since).ToList();
        var scored = recent.Where(i => i.Sentiment is not null).ToList();
        return new MarketNewsSummary
        {
            WindowMinutes = SummaryWindowMinutes,
            Count = recent.Count,
            Scored = scored.Count,
            AvgSentiment = scored.Count > 0 ? Math.Round(scored.Average(i => i.Sentiment!.Value), 2) : null,
            Fed = recent.Count(i => i.Tags.Contains("美联储")),
            Geo = recent.Count(i => i.Tags.Contains("地缘")),
            Macro = recent.Count(i => i.Tags.Contains("数据"))
        };
    }

    private static Regex Kw(string alternatives) =>
        new($@"\b({alternatives})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
