using System.Text.Json;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>Nasdaq 报价 + 历史财报 surprise。</summary>
public class NasdaqCompanyClient(IHttpClientFactory httpFactory, ILogger<NasdaqCompanyClient>? logger = null)
    : ICompanyFundamentalsClient
{
    public async Task<CompanySnapshot> GetAsync(string symbol, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient("ibtrade-intel");
        var quoteTask = LoadQuote(http, symbol, ct);
        var earnTask = LoadEarnings(http, symbol, ct);
        await Task.WhenAll(quoteTask, earnTask);
        return new CompanySnapshot(quoteTask.Result.Quote, earnTask.Result.Rows, quoteTask.Result.Source, earnTask.Result.Source);
    }

    private async Task<(UnderlyingQuoteDto? Quote, DataSourceStatusDto Source)> LoadQuote(
        HttpClient http, string symbol, CancellationToken ct)
    {
        var url = $"https://api.nasdaq.com/api/quote/{Uri.EscapeDataString(symbol)}/info?assetclass=stocks";
        try
        {
            var json = await IntelHttp.GetStringAsync(http, url, logger, ct);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            var name = data.TryGetProperty("companyName", out var n) ? n.GetString() : null;
            var exch = data.TryGetProperty("exchange", out var e) ? e.GetString() : null;
            var status = data.TryGetProperty("marketStatus", out var s) ? s.GetString() : null;
            JsonElement primary = default;
            data.TryGetProperty("primaryData", out primary);
            var last = IntelHttp.ParseDec(Str(primary, "lastSalePrice"));
            var chg = IntelHttp.ParseDec(Str(primary, "netChange"));
            var pct = IntelHttp.ParseDec(Str(primary, "percentageChange"));
            var vol = Str(primary, "volume");
            var ts = Str(primary, "lastTradeTimestamp");
            return (
                new UnderlyingQuoteDto(symbol, name, last, chg, pct, vol, status, ts, exch),
                new DataSourceStatusDto("nasdaq-quote", "Nasdaq Quote", "ok", status, url));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Nasdaq 报价失败 {Symbol}", symbol);
            return (null, new DataSourceStatusDto("nasdaq-quote", "Nasdaq Quote", "error",
                IntelHttp.Clip(ex.GetBaseException().Message), url));
        }
    }

    private async Task<(IReadOnlyList<EarningsRowDto> Rows, DataSourceStatusDto Source)> LoadEarnings(
        HttpClient http, string symbol, CancellationToken ct)
    {
        var url = $"https://api.nasdaq.com/api/company/{Uri.EscapeDataString(symbol)}/earnings-surprise";
        try
        {
            var json = await IntelHttp.GetStringAsync(http, url, logger, ct);
            using var doc = JsonDocument.Parse(json);
            var rows = new List<EarningsRowDto>();
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("earningsSurpriseTable", out var table)
                && table.TryGetProperty("rows", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in arr.EnumerateArray())
                {
                    rows.Add(new EarningsRowDto(
                        Str(row, "fiscalQtrEnd") ?? "",
                        Str(row, "dateReported") ?? "",
                        AsDec(row, "eps"),
                        IntelHttp.ParseDec(Str(row, "consensusForecast")),
                        IntelHttp.ParseDec(Str(row, "percentageSurprise"))));
                }
            }
            return (rows, new DataSourceStatusDto("nasdaq-earnings", "Nasdaq Earnings Surprise", "ok",
                $"{rows.Count} 季", url));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Nasdaq 财报失败 {Symbol}", symbol);
            return ([], new DataSourceStatusDto("nasdaq-earnings", "Nasdaq Earnings Surprise", "error",
                IntelHttp.Clip(ex.GetBaseException().Message), url));
        }
    }

    private static string? Str(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static decimal? AsDec(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        return IntelHttp.ParseDec(v.ToString());
    }
}
