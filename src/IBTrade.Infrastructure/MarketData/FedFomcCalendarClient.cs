using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using IBTrade.Application.MarketData;

namespace IBTrade.Infrastructure.MarketData;

/// <summary>
/// 解析 federalreserve.gov 的 FOMC 日程页。与 FRED 一样走 curl --noproxy，避开进程代理。
/// 会议跨月时（如 April/May 30-1）取后一个月与最后一天。
/// </summary>
public partial class FedFomcCalendarClient : IMacroCalendar
{
    private const string Url = "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm";

    public async Task<(IReadOnlyList<DateOnly> Dates, string? Error)> GetFomcDecisionDatesAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "curl",
                ArgumentList = { "-sS", "-m", "15", "--noproxy", "*", "-A", "Mozilla/5.0", Url },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["HTTPS_PROXY"] = "";
            psi.Environment["HTTP_PROXY"] = "";
            psi.Environment["ALL_PROXY"] = "";

            using var proc = Process.Start(psi);
            if (proc is null) return ([], "无法启动 curl 拉取美联储日程");
            var html = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                return ([], string.IsNullOrWhiteSpace(err) ? $"curl exit {proc.ExitCode}" : err.Trim());
            }

            var dates = Parse(html);
            return dates.Count == 0 ? ([], "美联储日程页没有解析出会议日期") : (dates, null);
        }
        catch (Exception ex)
        {
            return ([], ex.GetBaseException().Message);
        }
    }

    internal static List<DateOnly> Parse(string html)
    {
        var result = new List<DateOnly>();
        var headers = YearHeader().Matches(html);
        for (var i = 0; i < headers.Count; i++)
        {
            var year = int.Parse(headers[i].Groups[1].Value, CultureInfo.InvariantCulture);
            var start = headers[i].Index;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : html.Length;
            foreach (Match m in Meeting().Matches(html[start..end]))
            {
                var month = m.Groups[1].Value.Split('/')[^1].Trim();
                var days = Regex.Matches(m.Groups[2].Value, @"\d+");
                if (days.Count == 0) continue;
                if (!DateTime.TryParseExact(month, "MMMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mo)
                    && !DateTime.TryParseExact(month, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out mo))
                    continue;
                var day = int.Parse(days[^1].Value, CultureInfo.InvariantCulture);
                if (day < 1 || day > DateTime.DaysInMonth(year, mo.Month)) continue;
                result.Add(new DateOnly(year, mo.Month, day));
            }
        }
        return result.Distinct().OrderBy(d => d).ToList();
    }

    [GeneratedRegex(@"(\d{4}) FOMC Meetings")]
    private static partial Regex YearHeader();

    [GeneratedRegex(@"fomc-meeting__month[^>]*>\s*<strong>([A-Za-z/]+)</strong>.*?fomc-meeting__date[^>]*>([^<]+)<", RegexOptions.Singleline)]
    private static partial Regex Meeting();
}
