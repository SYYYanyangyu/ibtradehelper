using System.Diagnostics;
using System.Globalization;
using IBTrade.Application.MarketData;
using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Infrastructure.MarketData;

/// <summary>
/// 美联储 FRED 官方日线。走 curl --noproxy，避开进程里 HTTPS_PROXY 把政府站打挂。
/// </summary>
public class FredIndexClient : IOfficialIndexQuotes
{
    public async Task<(IReadOnlyList<CandleDto> Bars, string? Error)> GetFredAsync(
        string seriesId, CancellationToken ct)
    {
        try
        {
            var url = $"https://fred.stlouisfed.org/graph/fredgraph.csv?id={Uri.EscapeDataString(seriesId)}";
            var psi = new ProcessStartInfo
            {
                FileName = "curl",
                ArgumentList = { "-sS", "-m", "12", "--noproxy", "*", url },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["HTTPS_PROXY"] = "";
            psi.Environment["HTTP_PROXY"] = "";
            psi.Environment["ALL_PROXY"] = "";

            using var proc = Process.Start(psi);
            if (proc is null)
                return ([], "无法启动 curl 拉取 FRED");

            var text = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                return ([], string.IsNullOrWhiteSpace(err) ? $"curl exit {proc.ExitCode}" : err.Trim());
            }

            var bars = ParseCsv(text);
            return bars.Count == 0
                ? ([], $"FRED {seriesId} 没有有效收盘价")
                : (bars, null);
        }
        catch (Exception ex)
        {
            return ([], ex.GetBaseException().Message);
        }
    }

    private static List<CandleDto> ParseCsv(string text)
    {
        var bars = new List<CandleDto>();
        using var reader = new StringReader(text);
        reader.ReadLine();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var day))
                continue;
            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var close) || close <= 0)
                continue;
            bars.Add(new CandleDto(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc), close, close, close, close, 0));
        }
        return bars;
    }
}
