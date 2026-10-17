using System.Text.Json;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>
/// 热门界定：Nasdaq 官方「成交额最活跃」MostActiveByDollarVolume。
/// 不用成交股数（仙股会占满），也不用手写 FAANG 名单。
/// </summary>
public class NasdaqHotNamesClient(IHttpClientFactory httpFactory, ILogger<NasdaqHotNamesClient>? logger = null)
    : IHotOptionsUniverse
{
    private const string Url = "https://api.nasdaq.com/api/marketmovers?assetclass=stocks";
    private const string Rule =
        "Nasdaq 官方：成交额最活跃 + 纳指100异动。不用手写名单，点选即可看期权。";

    private static readonly object Gate = new();
    private static HotUniverseDto? Cache;
    private static DateTime CacheUntil;

    public async Task<HotUniverseDto> GetAsync(string? symbol, CancellationToken ct = default)
    {
        HotUniverseDto baseList;
        lock (Gate)
        {
            if (Cache is { } hit && CacheUntil > DateTime.UtcNow)
            {
                baseList = hit;
                return Attach(baseList, symbol);
            }
        }

        var http = httpFactory.CreateClient("ibtrade-intel");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var json = await IntelHttp.GetStringAsync(http, Url, logger, cts.Token);
            baseList = Parse(json);
            lock (Gate)
            {
                Cache = baseList;
                CacheUntil = DateTime.UtcNow.AddMinutes(15);
            }
            return Attach(baseList, symbol);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Nasdaq 成交额活跃榜失败");
            var err = new HotUniverseDto(
                DateTime.UtcNow.ToString("o"),
                Rule,
                Url,
                [],
                false,
                null,
                new DataSourceStatusDto("hot", "Nasdaq dollar volume", "error",
                    IntelHttp.Clip(ex.GetBaseException().Message), Url));
            return Attach(err, symbol);
        }
    }

    private static HotUniverseDto Attach(HotUniverseDto list, string? symbol)
    {
        var sym = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
        var match = sym is null
            ? null
            : list.Names.FirstOrDefault(n => string.Equals(n.Symbol, sym, StringComparison.OrdinalIgnoreCase));
        return list with
        {
            CurrentIsHot = match is not null,
            CurrentRank = match?.Rank
        };
    }

    private static HotUniverseDto Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var stocks = doc.RootElement.GetProperty("data").GetProperty("STOCKS");
        var dollar = ParseTable(stocks, "MostActiveByDollarVolume", "dollar");
        var n100 = ParseTable(stocks, "Nasdaq100Movers", "n100");
        var asOf = ReadAsOf(stocks, "MostActiveByDollarVolume")
                   ?? ReadAsOf(stocks, "Nasdaq100Movers")
                   ?? "";

        var names = new List<HotNameDto>(dollar);
        var seen = new HashSet<string>(dollar.Select(x => x.Symbol), StringComparer.OrdinalIgnoreCase);
        foreach (var row in n100)
        {
            if (!seen.Add(row.Symbol)) continue;
            names.Add(row);
        }

        return new HotUniverseDto(
            asOf,
            Rule,
            Url,
            names,
            false,
            null,
            new DataSourceStatusDto(
                "hot",
                "Nasdaq movers",
                names.Count > 0 ? "ok" : "error",
                names.Count > 0 ? $"{names.Count} 只 · {asOf}" : "榜单为空",
                Url));
    }

    private static string? ReadAsOf(JsonElement stocks, string table)
    {
        if (!stocks.TryGetProperty(table, out var node)) return null;
        return node.TryGetProperty("dataAsOf", out var a) ? a.GetString() : null;
    }

    private static List<HotNameDto> ParseTable(JsonElement stocks, string table, string bucket)
    {
        var names = new List<HotNameDto>();
        if (!stocks.TryGetProperty(table, out var node) ||
            !node.TryGetProperty("table", out var tbl) ||
            !tbl.TryGetProperty("rows", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            return names;

        foreach (var row in rows.EnumerateArray())
        {
            var sym = row.TryGetProperty("symbol", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(sym)) continue;
            var name = row.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (IsJunk(sym, name)) continue;
            var last = IntelHttp.ParseDec(row.TryGetProperty("lastSalePrice", out var p) ? p.GetString() : null);
            var rawChg = row.TryGetProperty("change", out var c) ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(rawChg) || !rawChg.Contains('%', StringComparison.Ordinal))
                rawChg = row.TryGetProperty("lastSaleChange", out var c2) ? c2.GetString() : rawChg;
            var chg = IntelHttp.ParseDec(rawChg);
            names.Add(new HotNameDto(
                names.Count + 1,
                sym.Trim().ToUpperInvariant(),
                name,
                last,
                chg,
                Kind(name),
                bucket));
        }
        return names;
    }

    private static bool IsJunk(string symbol, string? name)
    {
        var n = name ?? "";
        if (n.Contains("Warrant", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("Right", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("Unit", StringComparison.OrdinalIgnoreCase) &&
            !n.Contains("United", StringComparison.OrdinalIgnoreCase)) return true;
        return symbol.EndsWith('W') || symbol.EndsWith("WS", StringComparison.OrdinalIgnoreCase);
    }

    private static string Kind(string? name)
    {
        var n = name ?? "";
        if (n.Contains("ETF", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Trust", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("SPDR", StringComparison.OrdinalIgnoreCase))
            return "etf";
        return "stock";
    }
}
