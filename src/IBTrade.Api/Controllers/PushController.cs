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

    /// <summary>
    /// 订阅推送通知。公开端点，无需认证。
    /// 返回服务器生成的所有权令牌，客户端必须安全存储此令牌用于后续操作。
    /// </summary>
    [HttpPost("subscription")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request, CancellationToken ct)
    {
        try
        {
            var response = await pushService.SubscribeAsync(request, ct);
            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "订阅推送失败");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 取消推送订阅。需要所有权令牌验证。
    /// </summary>
    [HttpDelete("subscription")]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request, CancellationToken ct)
    {
        try
        {
            await pushService.UnsubscribeAsync(request.Endpoint, request.OwnershipToken, ct);
            return Ok(new { status = "unsubscribed" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "取消订阅失败");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 更新推送偏好。需要所有权令牌验证。
    /// </summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdatePreferencesRequest request,
        CancellationToken ct)
    {
        try
        {
            await pushService.UpdatePreferencesAsync(request.Endpoint, request.OwnershipToken, request.Preferences, ct);
            return Ok(new { status = "updated" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
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
