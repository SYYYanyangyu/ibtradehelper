using IBTrade.Application.Push;
using IBTrade.Application.Push.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// 推送管理控制器 - 仅供管理员使用，受 ApiKeyAuthMiddleware 保护。
/// 用于手动触发推送、查看统计信息等管理操作。
/// </summary>
[ApiController]
[Route("api/push/admin")]
public class PushAdminController(
    IPushNotificationService pushService,
    FinanceBriefStore briefStore,
    ILogger<PushAdminController> logger) : ControllerBase
{
    /// <summary>
    /// 手动触发定时简报推送（测试用）。
    /// 如果当前有缓存的简报，会提取前几条新闻作为摘要。
    /// </summary>
    [HttpPost("trigger-brief")]
    public async Task<IActionResult> TriggerBrief(CancellationToken ct)
    {
        try
        {
            var briefJson = briefStore.Current;
            if (!string.IsNullOrWhiteSpace(briefJson))
            {
                var brief = System.Text.Json.JsonSerializer.Deserialize<dynamic>(briefJson);
                var summary = ExtractBriefSummary(brief);
                await pushService.SendScheduledBriefWithContentAsync(summary, ct);
            }
            else
            {
                await pushService.SendScheduledBriefAsync(ct);
            }
            
            return Ok(new { status = "sent", message = "定时简报推送已触发" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "触发定时简报失败");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// 手动发送突发新闻推送。
    /// </summary>
    [HttpPost("trigger-breaking-news")]
    public async Task<IActionResult> TriggerBreakingNews(
        [FromBody] BreakingNewsRequest request,
        CancellationToken ct)
    {
        try
        {
            await pushService.SendBreakingNewsAsync(
                request.Title,
                request.Body,
                request.Url,
                ct
            );
            
            return Ok(new { status = "sent", message = "突发新闻推送已发送" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送突发新闻失败");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static string ExtractBriefSummary(dynamic? brief)
    {
        if (brief == null) return "查看最新的市场动态和重要资讯";
        
        try
        {
            var sections = brief.GetProperty("sections");
            if (sections.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var firstSection = sections.EnumerateArray().FirstOrDefault();
                if (firstSection.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    var items = firstSection.GetProperty("items");
                    var firstItem = items.EnumerateArray().FirstOrDefault();
                    if (firstItem.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                    {
                        var headline = firstItem.GetProperty("headline").GetString();
                        return headline ?? "查看最新的市场动态";
                    }
                }
            }
        }
        catch
        {
            // 解析失败，返回默认文本
        }
        
        return "查看最新的市场动态和重要资讯";
    }
}

public record BreakingNewsRequest(
    string Title,
    string Body,
    string? Url = null
);
