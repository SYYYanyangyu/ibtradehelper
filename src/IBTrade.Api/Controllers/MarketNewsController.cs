using IBTrade.Application.MarketData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBTrade.Api.Controllers;

public class GrokBotOptions
{
    public string WebhookUrl { get; set; } = "";
    public string WebhookKey { get; set; } = "";
    public string GitHubOwner { get; set; } = "SYYYanyangyu";
    public string GitHubRepo { get; set; } = "ibtradehelper";
    public string GitHubBranch { get; set; } = "main";
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
    private const string JsonPath = "frontend/public/feeds/finance-brief.json";

    /// <summary>
    /// 从 GitHub raw API 获取最新的简报 JSON（绕过本地文件缓存）
    /// </summary>
    [HttpGet("brief")]
    public async Task<IActionResult> GetBrief(CancellationToken ct)
    {
        try
        {
            var owner = string.IsNullOrWhiteSpace(_grokOptions.GitHubOwner) ? "SYYYanyangyu" : _grokOptions.GitHubOwner;
            var repo = string.IsNullOrWhiteSpace(_grokOptions.GitHubRepo) ? "ibtradehelper" : _grokOptions.GitHubRepo;
            var branch = string.IsNullOrWhiteSpace(_grokOptions.GitHubBranch) ? "main" : _grokOptions.GitHubBranch;
            
            var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{JsonPath}";
            
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.Add("Pragma", "no-cache");
            
            var response = await client.GetAsync(rawUrl, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GitHub raw API returned {StatusCode} for {Url}", response.StatusCode, rawUrl);
                return StatusCode((int)response.StatusCode, $"无法从 GitHub 获取简报：{response.StatusCode}");
            }
            
            var json = await response.Content.ReadAsStringAsync(ct);
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch finance brief from GitHub");
            return StatusCode(500, $"获取简报失败：{ex.Message}");
        }
    }

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
                action = "refresh_finance_brief"
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
