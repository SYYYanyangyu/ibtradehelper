using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>Nasdaq 官方延迟期权链（api.nasdaq.com）。</summary>
public class NasdaqOptionsChainClient(IHttpClientFactory httpFactory, ILogger<NasdaqOptionsChainClient>? logger = null)
    : IOptionsChainClient
{
    private const string SourceUrl = "https://api.nasdaq.com/api/quote/{0}/option-chain";

    public async Task<OptionsChainSnapshot> GetAsync(string symbol, string? expiry, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient("ibtrade-intel");
        var url = BuildUrl(symbol, expiry);
        try
        {
            var json = await IntelHttp.GetStringAsync(http, url, logger, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Nasdaq 期权链未返回 data。");

            var lastTrade = data.TryGetProperty("lastTrade", out var lt) ? lt.GetString() : null;
            var spot = ParseLastTradePrice(lastTrade);
            var table = data.GetProperty("table");
            var rowsEl = table.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array ? r : default;

            var filterExpiries = ReadFilterExpiries(data);
            var groups = new List<(string Expiry, List<OptionRowDto> Rows)>();
            string? current = null;
            List<OptionRowDto>? bucket = null;
            if (rowsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rowsEl.EnumerateArray())
                {
                    var group = Str(row, "expirygroup");
                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        current = NormalizeExpiry(group);
                        bucket = [];
                        groups.Add((current, bucket));
                        continue;
                    }
                    if (bucket is null || current is null) continue;
                    var strike = IntelHttp.ParseDec(Str(row, "strike"));
                    if (strike is null) continue;
                    bucket.Add(new OptionRowDto(
                        current,
                        strike.Value,
                        new OptionSideDto(
                            IntelHttp.ParseDec(Str(row, "c_Last")),
                            IntelHttp.ParseDec(Str(row, "c_Change")),
                            IntelHttp.ParseDec(Str(row, "c_Bid")),
                            IntelHttp.ParseDec(Str(row, "c_Ask")),
                            IntelHttp.ParseInt(Str(row, "c_Volume")),
                            IntelHttp.ParseInt(Str(row, "c_Openinterest"))),
                        new OptionSideDto(
                            IntelHttp.ParseDec(Str(row, "p_Last")),
                            IntelHttp.ParseDec(Str(row, "p_Change")),
                            IntelHttp.ParseDec(Str(row, "p_Bid")),
                            IntelHttp.ParseDec(Str(row, "p_Ask")),
                            IntelHttp.ParseInt(Str(row, "p_Volume")),
                            IntelHttp.ParseInt(Str(row, "p_Openinterest")))));
                }
            }

            var expiries = groups.Select(g => g.Expiry).Where(e => e.Length > 0)
                .Concat(filterExpiries)
                .Distinct()
                .OrderBy(e => e)
                .ToList();
            var selected = PickExpiry(expiries, expiry);
            var full = groups.FirstOrDefault(g => g.Expiry == selected).Rows ?? [];
            if (full.Count == 0 && groups.Count > 0)
            {
                selected = groups[0].Expiry;
                full = groups[0].Rows;
            }

            var sliced = SliceAroundAtm(full, spot, 14);
            var stats = BuildStats(full, sliced.Count, spot);
            var underlying = new UnderlyingQuoteDto(
                symbol, null, spot, null, null, null, null, lastTrade, "Nasdaq");

            return new OptionsChainSnapshot(
                underlying, expiries, selected, sliced, stats,
                    new DataSourceStatusDto("nasdaq-chain", "Nasdaq Option Chain", "ok",
                    $"延迟报价 · {full.Count} 行 · 展示 ATM±14", url));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Nasdaq 期权链失败 {Symbol}", symbol);
            return new OptionsChainSnapshot(
                null, [], null, [], new ChainStatsDto(0, 0, null, 0, 0, 0, 0, null, null),
                new DataSourceStatusDto("nasdaq-chain", "Nasdaq Option Chain", "error",
                    IntelHttp.Clip(ex.GetBaseException().Message), string.Format(SourceUrl, symbol)));
        }
    }

    private static string BuildUrl(string symbol, string? expiry)
    {
        var root = $"https://api.nasdaq.com/api/quote/{Uri.EscapeDataString(symbol)}/option-chain?assetclass=stocks";
        if (!string.IsNullOrWhiteSpace(expiry) && DateTime.TryParse(expiry, out _))
            return $"{root}&fromdate={Uri.EscapeDataString(expiry)}&todate={Uri.EscapeDataString(expiry)}";
        return root;
    }

    private static IReadOnlyList<string> ReadFilterExpiries(JsonElement data)
    {
        var list = new List<string>();
        if (!data.TryGetProperty("filterlist", out var filters) || filters.ValueKind != JsonValueKind.Object)
            return list;
        if (!filters.TryGetProperty("fromdate", out var fd) || !fd.TryGetProperty("filter", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
        {
            var val = item.TryGetProperty("value", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(val) || val == "all") continue;
            foreach (var part in val.Split('|'))
            {
                if (DateTime.TryParse(part, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    list.Add(dt.ToString("yyyy-MM-dd"));
            }
        }
        return list.Distinct().ToList();
    }

    private static string? PickExpiry(IReadOnlyList<string> expiries, string? requested)
    {
        if (expiries.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var hit = expiries.FirstOrDefault(e =>
                e.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        var today = DateTime.UtcNow.Date;
        return expiries
            .Select(e => (e, d: DateTime.TryParse(e, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt.Date : DateTime.MaxValue))
            .OrderBy(x => x.d < today ? 1 : 0)
            .ThenBy(x => x.d)
            .Select(x => x.e)
            .First();
    }

    private static IReadOnlyList<OptionRowDto> SliceAroundAtm(IReadOnlyList<OptionRowDto> rows, decimal? spot, int radius)
    {
        if (rows.Count == 0) return rows;
        if (spot is null or 0) return rows.Take(radius * 2 + 1).ToList();
        var idx = 0;
        var best = decimal.MaxValue;
        for (var i = 0; i < rows.Count; i++)
        {
            var d = Math.Abs(rows[i].Strike - spot.Value);
            if (d < best) { best = d; idx = i; }
        }
        var start = Math.Max(0, idx - radius);
        var end = Math.Min(rows.Count, idx + radius + 1);
        return rows.Skip(start).Take(end - start).ToList();
    }

    private static ChainStatsDto BuildStats(IReadOnlyList<OptionRowDto> full, int shown, decimal? spot)
    {
        long cv = 0, pv = 0, co = 0, po = 0;
        foreach (var r in full)
        {
            cv += r.Call.Volume ?? 0;
            pv += r.Put.Volume ?? 0;
            co += r.Call.OpenInterest ?? 0;
            po += r.Put.OpenInterest ?? 0;
        }
        decimal? atm = null;
        if (spot is > 0 && full.Count > 0)
            atm = full.MinBy(r => Math.Abs(r.Strike - spot.Value))?.Strike;
        return new ChainStatsDto(
            shown, full.Count, atm, cv, pv, co, po,
            cv > 0 ? Math.Round((decimal)pv / cv, 3) : null,
            co > 0 ? Math.Round((decimal)po / co, 3) : null);
    }

    private static string NormalizeExpiry(string raw)
    {
        raw = raw.Trim();
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");
        return raw;
    }

    private static decimal? ParseLastTradePrice(string? lastTrade)
    {
        if (string.IsNullOrWhiteSpace(lastTrade)) return null;
        var m = Regex.Match(lastTrade, @"\$([0-9]+(?:\.[0-9]+)?)");
        return m.Success ? IntelHttp.ParseDec(m.Groups[1].Value) : null;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
