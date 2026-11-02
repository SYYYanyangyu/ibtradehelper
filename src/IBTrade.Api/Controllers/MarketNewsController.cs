using IBTrade.Application.MarketData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBTrade.Api.Controllers;

public class GrokBotOptions
{
    public string WebhookUrl { get; set; } = "";
    public string WebhookKey { get; set; } = "";
}

[ApiController]
[Route("api/[controller]")]
public class NewsController(
    MarketNewsService news,
    IOptions<GrokBotOptions> grokOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<NewsController> logger) : ControllerBase
{
    private readonly GrokBotOptions _grokOptions = grokOptions.Value;

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_grokOptions.WebhookUrl))
        {
            logger.LogWarning("Grok Bot webhook URL not configured");
            return StatusCode(500, "Grok Bot webhook URL 未配置，请在 appsettings 中设置 GrokBot:WebhookUrl");
        }

        if (string.IsNullOrWhiteSpace(_grokOptions.WebhookKey))
        {
            logger.LogWarning("Grok Bot webhook key not configured");
            return StatusCode(500, "Grok Bot webhook key 未配置，请在 appsettings 中设置 GrokBot:WebhookKey");
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_grokOptions.WebhookKey}");

            var payload = new
            {
                action = "refresh_finance_brief",
                language = "zh-CN"
            };

            var response = await client.PostAsJsonAsync(_grokOptions.WebhookUrl, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Grok Bot webhook returned {StatusCode}: {Error}", response.StatusCode, error);
                return StatusCode((int)response.StatusCode, $"Grok Bot 返回错误: {error}");
            }

            logger.LogInformation("Successfully triggered Grok Bot finance brief refresh");
            return Accepted();
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to call Grok Bot webhook");
            return StatusCode(500, $"调用 Grok Bot webhook 失败: {ex.Message}");
        }
    }
}

[ApiController]
[Route("api/market-news")]
public class MarketNewsController(MarketNewsService news) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string symbol = "QQQ", CancellationToken ct = default) =>
        Ok(await news.GetAsync(symbol, ct));
}
