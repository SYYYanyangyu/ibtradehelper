using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>公司新闻：Yahoo Finance RSS 标题 + Google News RSS（只取标题/链接/时间）。</summary>
public class NewsFeedClient(IHttpClientFactory httpFactory, ILogger<NewsFeedClient>? logger = null) : INewsFeedClient
{
    public async Task<(IReadOnlyList<NewsItemDto> Items, DataSourceStatusDto Source)> GetAsync(
        string symbol, string? companyName, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient("ibtrade-intel");
        var items = new List<NewsItemDto>();
        var errors = new List<string>();

        var yahooUrl = $"https://feeds.finance.yahoo.com/rss/2.0/headline?s={Uri.EscapeDataString(symbol)}&region=US&lang=en-US";
        try
        {
            var xml = await IntelHttp.GetStringAsync(http, yahooUrl, logger, ct, accept: "application/rss+xml");
            foreach (var it in IntelHttp.ParseRss(xml, 8))
                items.Add(new NewsItemDto(it.Title, it.Url, it.Published, "Yahoo Finance"));
        }
        catch (Exception ex)
        {
            errors.Add("Yahoo: " + IntelHttp.Clip(ex.GetBaseException().Message, 80));
            logger?.LogWarning(ex, "Yahoo 新闻 RSS 失败");
        }

        var ok = items.Count > 0;
        return (items.ToList(), new DataSourceStatusDto(
            "news", "Yahoo News RSS", ok ? "ok" : "error",
            ok ? $"{items.Count} 条标题" : string.Join("；", errors),
            yahooUrl));
    }
}
