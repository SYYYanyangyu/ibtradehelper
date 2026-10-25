using System.Collections.Concurrent;
using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.MarketData;

/// <summary>
/// 指数日线：优先美联储 FRED 官方收盘（美股三大）；恒生再试 IB Gateway。
/// </summary>
public class IndexHistoryCache(IOfficialIndexQuotes official, IHistoricalDataService hist)
{
    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<(IReadOnlyList<CandleDto> Bars, string? Error)> GetAsync(
        string id,
        IReadOnlyList<IndexContract> attempts,
        string? fredSeriesId,
        string duration,
        CancellationToken ct)
    {
        var key = $"{id}|{duration}";
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < hit.Ttl)
            return (hit.Bars, hit.Error);

        if (attempts.Count == 0 && string.IsNullOrWhiteSpace(fredSeriesId))
        {
            const string hang = "恒生需 Gateway 港股历史；当前周末历史农场不可用";
            _cache[key] = new Entry(DateTime.UtcNow, TimeSpan.FromMinutes(10), [], hang);
            return ([], hang);
        }

        if (!string.IsNullOrWhiteSpace(fredSeriesId))
        {
            var fred = await official.GetFredAsync(fredSeriesId, ct);
            if (fred.Bars.Count > 0)
            {
                _cache[key] = new Entry(DateTime.UtcNow, TimeSpan.FromHours(6), fred.Bars, null);
                return (fred.Bars, null);
            }

            if (IsCancel(fred.Error))
                return ([], fred.Error);
            _cache[key] = new Entry(DateTime.UtcNow, TimeSpan.FromMinutes(10), [], fred.Error ?? "FRED 无数据");
            return ([], fred.Error);
        }

        Exception? last = null;
        foreach (var c in attempts)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(10));
                var bars = await hist.GetBarsAsync(
                    c.Symbol, c.Exchange, c.Currency, duration, "1 day", linked.Token, c.SecType, "TRADES");
                if (bars.Count > 0)
                {
                    _cache[key] = new Entry(DateTime.UtcNow, TimeSpan.FromMinutes(45), bars, null);
                    return (bars, null);
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        var err = last?.GetBaseException().Message ?? "Gateway 未返回该指数日线";
        if (IsCancel(err))
            return ([], err);
        _cache[key] = new Entry(DateTime.UtcNow, TimeSpan.FromMinutes(5), [], err);
        return ([], err);
    }

    private static bool IsCancel(string? err) =>
        !string.IsNullOrWhiteSpace(err)
        && (err.Contains("canceled", StringComparison.OrdinalIgnoreCase)
            || err.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
            || err.Contains("取消", StringComparison.Ordinal));

    private readonly record struct Entry(
        DateTime At, TimeSpan Ttl, IReadOnlyList<CandleDto> Bars, string? Error);

    public readonly record struct IndexContract(string Symbol, string Exchange, string Currency, string SecType = "IND");
}
