using System.Text.Json;
using IBTrade.Application.MarketData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBTrade.Api.Controllers;

public class GrokBotOptions
{
    public string WebhookUrl { get; set; } = "";
    public string WebhookKey { get; set; } = "";
}

/// <summary>
/// 本次进程里 Grok Bot 回传的最新简报。内容仍是 Bot 从 X 整理的 JSON，不从 GitHub 读取。
/// </summary>
public sealed class FinanceBriefStore
{
    private readonly object _gate = new();
    private string? _json;

    public string? Current
    {
        get { lock (_gate) return _json; }
    }

    public void Set(string json)
    {
        lock (_gate) _json = json;
    }
}

[ApiController]
[Route("api/[controller]")]
public class NewsController(
    IOptions<GrokBotOptions> grokOptions,
    IHttpClientFactory httpClientFactory,
    FinanceBriefStore store,
    ILogger<NewsController> logger) : ControllerBase
{
    private readonly GrokBotOptions _grokOptions = grokOptions.Value;

    /// <summary>返回本次进程中 Bot 回传的简报。没有回传时 204，页面继续用本地快照。</summary>
    [HttpGet("brief")]
    public IActionResult GetBrief()
    {
        var json = store.Current;
        if (string.IsNullOrWhiteSpace(json)) return NoContent();
        return Content(json, "application/json");
    }

    /// <summary>
    /// Grok Bot 读完 X 后，把整理好的简报 JSON POST 回来。
    /// 鉴权与触发 webhook 相同：Authorization: Bearer &lt;GrokBot:WebhookKey&gt;。
    /// </summary>
    [HttpPost("brief")]
    public async Task<IActionResult> Ingest(CancellationToken ct)
    {
        if (!WebhookKeyMatches())
            return Unauthorized("需要 Authorization: Bearer <GrokBot:WebhookKey>");

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (!LooksLikeBrief(body))
            return BadRequest("正文需要是简报 JSON（含 updatedAt 与 sections）");

        store.Set(body);
        logger.LogInformation("Stored finance brief pushed by Grok Bot");
        return Ok(new { status = "stored" });
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
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {_grokOptions.WebhookKey}");

            var payload = new
            {
                action = "refresh_finance_brief"
            };

            var response = await client.PostAsJsonAsync(_grokOptions.WebhookUrl, payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Grok Bot webhook returned {StatusCode}: {Error}", response.StatusCode, body);
                return StatusCode((int)response.StatusCode, $"Grok Bot 返回错误: {body}");
            }

            if (LooksLikeBrief(body))
            {
                store.Set(body);
                logger.LogInformation("Grok Bot returned a finance brief in the webhook response");
                return Content(body, "application/json");
            }

            logger.LogInformation("Triggered Grok Bot finance brief refresh; waiting for the bot to POST the JSON back");
            return Accepted(new
            {
                status = "accepted",
                message = "已通知 Grok Bot 从 X 更新简报，等待它回传 JSON"
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to call Grok Bot webhook");
            return StatusCode(500, $"调用 Grok Bot webhook 失败: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Grok Bot webhook timed out");
            return StatusCode(504, "调用 Grok Bot webhook 超时");
        }
    }

    private bool WebhookKeyMatches()
    {
        var expected = _grokOptions.WebhookKey?.Trim();
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var provided = auth["Bearer ".Length..].Trim();
        return string.Equals(provided, expected, StringComparison.Ordinal);
    }

    private static bool LooksLikeBrief(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var hasUpdated = false;
            var hasSections = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("updatedAt") || prop.Name.Equals("updatedAt", StringComparison.OrdinalIgnoreCase))
                    hasUpdated = true;
                if ((prop.NameEquals("sections") || prop.Name.Equals("sections", StringComparison.OrdinalIgnoreCase))
                    && prop.Value.ValueKind == JsonValueKind.Array)
                    hasSections = true;
            }
            return hasUpdated && hasSections;
        }
        catch (JsonException)
        {
            return false;
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
