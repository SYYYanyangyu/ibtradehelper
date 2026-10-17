using System.Text.Json;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>Manifold 预测市场（与 Polymarket 同类：事件概率可交易）。公开 API，无需 Key。</summary>
public class ManifoldClient(IHttpClientFactory httpFactory, ILogger<ManifoldClient>? logger = null)
{
    public async Task<(IReadOnlyList<PredictionMarketDto> Markets, DataSourceStatusDto Source)> SearchAsync(
        string symbol, string? companyName, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient("ibtrade-intel");
        var terms = new[]
        {
            $"{symbol} stock",
            "Fed cut October FOMC"
        };

        var rows = new List<PredictionMarketDto>();
        string? err = null;
        foreach (var term in terms)
        {
            var url = $"https://api.manifold.markets/v0/search-markets?term={Uri.EscapeDataString(term)}&limit=6";
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                var json = await IntelHttp.GetStringAsync(http, url, logger, cts.Token);
                Parse(json, rows);
            }
            catch (Exception ex)
            {
                err = IntelHttp.Clip(ex.GetBaseException().Message);
                logger?.LogWarning(ex, "Manifold 查询失败 {Term}", term);
            }
        }

        var dedup = rows.GroupBy(r => r.Question, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).Take(6).ToList();
        var ok = dedup.Count > 0;
        return (dedup, new DataSourceStatusDto("manifold", "Manifold", ok ? "ok" : "error",
            ok ? $"{dedup.Count} 个市场" : err ?? "无结果",
            "https://api.manifold.markets"));
    }

    private static void Parse(string json, List<PredictionMarketDto> into)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var q = el.TryGetProperty("question", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(q)) continue;
            decimal? prob = null;
            if (el.TryGetProperty("probability", out var p) && p.ValueKind == JsonValueKind.Number)
                prob = p.GetDecimal();
            var url = el.TryGetProperty("url", out var u) ? u.GetString() : null;
            var close = el.TryGetProperty("closeTime", out var c) && c.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(c.GetInt64()).UtcDateTime.ToString("o")
                : null;
            decimal? vol = null;
            if (el.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number)
                vol = v.GetDecimal();
            into.Add(new PredictionMarketDto(q, prob, url, "Manifold", close, Volume: vol));
        }
    }
}
