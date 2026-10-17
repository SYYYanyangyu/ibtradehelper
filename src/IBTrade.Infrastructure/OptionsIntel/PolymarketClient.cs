using System.Net;
using System.Text.Json;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>
/// Polymarket Gamma 公开 API。部分出口 IP 会被 Cloudflare 451/1026，失败时如实返回错误，不编造盘口。
/// </summary>
public class PolymarketClient(IHttpClientFactory httpFactory, ILogger<PolymarketClient>? logger = null)
{
    public async Task<(IReadOnlyList<PredictionMarketDto> Markets, DataSourceStatusDto Source)> SearchAsync(
        string symbol, string? companyName, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient("ibtrade-intel");
        var q = string.IsNullOrWhiteSpace(companyName) ? $"{symbol} fed" : $"{symbol} {companyName}";
        var markets = new List<PredictionMarketDto>();
        string? lastErr = null;
        var url = $"https://gamma-api.polymarket.com/events?limit=12&active=true&closed=false";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            var json = await IntelHttp.GetStringAsync(http, url, logger, cts.Token);
            ParseEvents(json, markets);
            var filtered = markets.Where(m =>
                ContainsAny(m.Question, symbol, companyName, "fed", "fomc", "cpi", "rate", "earnings")).ToList();
            if (filtered.Count > 0) markets = filtered;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.UnavailableForLegalReasons
                                              || (int?)ex.StatusCode == 451)
        {
            lastErr = "Polymarket 对该出口 IP 返回 451（Cloudflare 拦截），未拿到盘口。";
        }
        catch (Exception ex)
        {
            lastErr = IntelHttp.Clip(ex.GetBaseException().Message);
            logger?.LogWarning(ex, "Polymarket 查询失败 {Q}", q);
        }

        var ok = markets.Count > 0;
        return (
            Dedup(markets),
            new DataSourceStatusDto("polymarket", "Polymarket", ok ? "ok" : "error",
                ok ? $"{markets.Count} 个相关市场" : lastErr ?? "无结果",
                "https://gamma-api.polymarket.com"));
    }

    private static void ParseSearch(string json, List<PredictionMarketDto> into)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            ParseEvents(json, into);
            return;
        }
        foreach (var key in new[] { "events", "markets" })
        {
            if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                ReadArray(arr, into);
        }
    }

    private static void ParseEvents(string json, List<PredictionMarketDto> into)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            ReadArray(doc.RootElement, into);
    }

    private static void ReadArray(JsonElement arr, List<PredictionMarketDto> into)
    {
        foreach (var el in arr.EnumerateArray())
        {
            var title = el.TryGetProperty("title", out var t) ? t.GetString()
                : el.TryGetProperty("question", out var q) ? q.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;
            decimal? prob = null;
            if (el.TryGetProperty("lastTradePrice", out var p) && p.ValueKind == JsonValueKind.Number)
                prob = p.GetDecimal();
            else if (el.TryGetProperty("outcomePrices", out var op))
                prob = FirstPrice(op);
            var slug = el.TryGetProperty("slug", out var s) ? s.GetString() : null;
            var url = string.IsNullOrWhiteSpace(slug) ? null : "https://polymarket.com/event/" + slug;
            var end = el.TryGetProperty("endDate", out var e) ? e.GetString()
                : el.TryGetProperty("end_date_iso", out var e2) ? e2.GetString() : null;
            into.Add(new PredictionMarketDto(title, prob, url, "Polymarket", end));
            if (into.Count >= 16) return;
        }
    }

    private static decimal? FirstPrice(JsonElement op)
    {
        if (op.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var inner = JsonDocument.Parse(op.GetString() ?? "[]");
                if (inner.RootElement.ValueKind == JsonValueKind.Array && inner.RootElement.GetArrayLength() > 0)
                    return IntelHttp.ParseDec(inner.RootElement[0].ToString());
            }
            catch { /* ignore */ }
        }
        if (op.ValueKind == JsonValueKind.Array && op.GetArrayLength() > 0)
            return IntelHttp.ParseDec(op[0].ToString());
        return null;
    }

    private static IReadOnlyList<PredictionMarketDto> Dedup(List<PredictionMarketDto> rows) =>
        rows.GroupBy(r => r.Question, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(12).ToList();

    private static bool ContainsAny(string? text, params string?[] keys)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var k in keys)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (text.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
