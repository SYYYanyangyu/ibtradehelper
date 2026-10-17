using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

internal static class IntelHttp
{
    public static async Task<string> GetStringAsync(
        HttpClient http, string url, ILogger? logger, CancellationToken ct, string? accept = "application/json")
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(accept))
            req.Headers.TryAddWithoutValidation("Accept", accept);
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger?.LogWarning("情报源 HTTP {Code} {Url}", (int)resp.StatusCode, Sanitize(url));
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", null, resp.StatusCode);
        }
        return body;
    }

    public static IReadOnlyList<(string Title, string? Url, string? Published)> ParseRss(string xml, int take)
    {
        var doc = XDocument.Parse(xml);
        XNamespace? dc = doc.Root?.GetDefaultNamespace();
        var items = new List<(string, string?, string?)>();
        foreach (var item in doc.Descendants("item"))
        {
            var title = (item.Element("title")?.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title)) continue;
            var link = item.Element("link")?.Value?.Trim();
            var pub = item.Element("pubDate")?.Value?.Trim();
            items.Add((WebUtility.HtmlDecode(title), link, pub));
            if (items.Count >= take) break;
        }
        return items;
    }

    public static string Sanitize(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.GetLeftPart(UriPartial.Path);
        }
        catch
        {
            return url.Length > 80 ? url[..80] : url;
        }
    }

    public static decimal? ParseDec(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal).Replace("+", "", StringComparison.Ordinal);
        if (s is "--" or "N/A" or "-" or "n/a") return null;
        return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace(",", "", StringComparison.Ordinal);
        if (s is "--" or "N/A" or "-") return null;
        return int.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static string Clip(string? s, int max = 180)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
