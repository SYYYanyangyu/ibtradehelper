using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>美联储 FOMC 日历 + 货币政策 RSS + 美国财政部平均利率。</summary>
public class MacroDataClient(IHttpClientFactory httpFactory, ILogger<MacroDataClient>? logger = null) : IMacroDataClient
{
    private const string FomcUrl = "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm";
    private const string TreasuryUrl =
        "https://api.fiscaldata.treasury.gov/services/api/fiscal_service/v2/accounting/od/avg_interest_rates?sort=-record_date&page[size]=20";

    public async Task<MacroSnapshot> GetAsync(CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient("ibtrade-intel");
        var fedTask = LoadFed(http, ct);
        var ustTask = LoadTreasury(http, ct);
        await Task.WhenAll(fedTask, ustTask);
        return new MacroSnapshot(fedTask.Result.Events, ustTask.Result.Rates, fedTask.Result.Source, ustTask.Result.Source);
    }

    private async Task<(IReadOnlyList<FedEventDto> Events, DataSourceStatusDto Source)> LoadFed(
        HttpClient http, CancellationToken ct)
    {
        var events = new List<FedEventDto>();
        string? err = null;
        try
        {
            var html = await IntelHttp.GetStringAsync(http, FomcUrl, logger, ct, accept: "text/html");
            events.AddRange(ParseFomc(html));
        }
        catch (Exception ex)
        {
            err = ex.GetBaseException().Message;
            logger?.LogWarning(ex, "FOMC 日历失败");
        }

        events = events.Where(e => e.Upcoming).Take(4).ToList();
        var ok = events.Count > 0;
        return (events, new DataSourceStatusDto("fed", "Federal Reserve", ok ? "ok" : "error",
            ok ? $"即将召开 FOMC {events.Count}" : IntelHttp.Clip(err),
            FomcUrl));
    }

    private async Task<(IReadOnlyList<TreasuryRateDto> Rates, DataSourceStatusDto Source)> LoadTreasury(
        HttpClient http, CancellationToken ct)
    {
        try
        {
            var json = await IntelHttp.GetStringAsync(http, TreasuryUrl, logger, ct);
            using var doc = JsonDocument.Parse(json);
            var rows = new List<TreasuryRateDto>();
            if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in arr.EnumerateArray())
                {
                    var desc = row.TryGetProperty("security_desc", out var s) ? s.GetString() : null;
                    if (string.IsNullOrWhiteSpace(desc)) continue;
                    if (desc is not ("Treasury Bills" or "Treasury Notes" or "Treasury Bonds" or "Treasury Inflation-Protected Securities (TIPS)"))
                        continue;
                    rows.Add(new TreasuryRateDto(
                        desc,
                        IntelHttp.ParseDec(row.TryGetProperty("avg_interest_rate_amt", out var r) ? r.GetString() : null),
                        row.TryGetProperty("record_date", out var d) ? d.GetString() : null));
                    if (rows.Count >= 6) break;
                }
            }
            return (rows, new DataSourceStatusDto("treasury", "US Treasury Fiscal Data", "ok",
                rows.FirstOrDefault()?.AsOf, TreasuryUrl));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "财政部利率失败");
            return ([], new DataSourceStatusDto("treasury", "US Treasury Fiscal Data", "error",
                IntelHttp.Clip(ex.GetBaseException().Message), TreasuryUrl));
        }
    }

    internal static IReadOnlyList<FedEventDto> ParseFomc(string html)
    {
        var list = new List<FedEventDto>();
        var yearBlocks = Regex.Split(html, @"(?=<h4[^>]*>\s*<a[^>]*>\s*20\d{2}\s+FOMC Meetings)");
        foreach (var block in yearBlocks)
        {
            var ym = Regex.Match(block, @"(20\d{2})\s+FOMC Meetings");
            if (!ym.Success) continue;
            var year = int.Parse(ym.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (Match m in Regex.Matches(block,
                         @"fomc-meeting__month[^>]*>\s*<strong>(?<month>[A-Za-z]+)</strong>.*?" +
                         @"fomc-meeting__date[^>]*>\s*(?<days>[\d\-]+)",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var month = m.Groups["month"].Value;
                var days = m.Groups["days"].Value.Trim();
                var lastDay = days.Contains('-', StringComparison.Ordinal) ? days.Split('-')[1] : days;
                DateTime? when = null;
                if (DateTime.TryParseExact($"{month} {lastDay} {year}", "MMMM d yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    when = dt;
                var upcoming = when is null || when.Value.Date >= DateTime.UtcNow.Date;
                var href = Regex.Match(m.Value, @"href=""(?<h>/newsevents/pressreleases/monetary[^""]+)""");
                var url = href.Success ? "https://www.federalreserve.gov" + href.Groups["h"].Value : FomcUrl;
                list.Add(new FedEventDto(
                    "fomc",
                    $"{year} {month} {days} FOMC",
                    when?.ToString("yyyy-MM-dd") ?? $"{year}-{month}-{days}",
                    url,
                    upcoming));
            }
        }
        return list
            .OrderByDescending(e => e.Upcoming)
            .ThenBy(e => e.Date)
            .Take(16)
            .ToList();
    }
}
