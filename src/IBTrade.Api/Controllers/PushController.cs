using IBTrade.Application.Push;
using IBTrade.Application.Push.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PushController(IPushNotificationService pushService, ILogger<PushController> logger) : ControllerBase
{
    [HttpGet("vapid-public-key")]
    public async Task<IActionResult> GetVapidPublicKey(CancellationToken ct)
    {
        try
        {
            var publicKey = await pushService.GetVapidPublicKeyAsync(ct);
            return Ok(new VapidPublicKeyResponse(publicKey));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "VAPID 公钥未配置");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("subscription")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request, CancellationToken ct)
    {
        try
        {
            await pushService.SubscribeAsync(request, ct);
            return Ok(new { status = "subscribed", endpoint = request.Endpoint });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "订阅推送失败");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 取消推送订阅。订阅端点作为身份验证 - 只有拥有端点的客户端可以取消订阅。
    /// </summary>
    [HttpDelete("subscription")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || request.Endpoint.Length < 50)
        {
            return BadRequest(new { error = "无效的订阅端点" });
        }

        try
        {
            await pushService.UnsubscribeAsync(request.Endpoint, ct);
            return Ok(new { status = "unsubscribed" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "取消订阅失败");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 获取推送偏好。订阅端点作为身份验证 - 只有拥有端点的客户端可以查询偏好。
    /// 端点是唯一的长随机字符串，知道端点即拥有该订阅的控制权。
    /// </summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences([FromQuery] string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length < 50)
        {
            return BadRequest(new { error = "无效的订阅端点" });
        }

        try
        {
            var preferences = await pushService.GetPreferencesAsync(endpoint, ct);
            return Ok(preferences);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取推送偏好失败");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// 更新推送偏好。订阅端点作为身份验证 - 只有拥有端点的客户端可以更新偏好。
    /// </summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromQuery] string endpoint,
        [FromBody] NotificationPreferences preferences,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length < 50)
        {
            return BadRequest(new { error = "无效的订阅端点" });
        }

        try
        {
            await pushService.UpdatePreferencesAsync(endpoint, preferences, ct);
            return Ok(new { status = "updated" });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "更新推送偏好失败");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 手动发送推送通知。需要 X-Api-Key 认证（管理员操作）。
    /// 此端点受 ApiKeyAuthMiddleware 保护，防止未授权的推送发送。
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendPush([FromBody] SendPushRequest request, CancellationToken ct)
    {
        try
        {
            await pushService.SendNotificationAsync(request, ct);
            return Ok(new { status = "sent" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送推送失败");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        try
        {
            var count = await pushService.GetActiveSubscriptionCountAsync(ct);
            return Ok(new { activeSubscriptions = count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取统计信息失败");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
