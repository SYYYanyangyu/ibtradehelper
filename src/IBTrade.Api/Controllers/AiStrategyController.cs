using System.Collections.Concurrent;
using IBTrade.Application.Ai;
using IBTrade.Application.Ai.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// AI 策略闭环 API：建议 / 建议+回测 / 强制演练部署。
/// 安全：白名单策略、参数钳制、部署永远 DryRun；AI 接口带简易频率限制。
/// </summary>
[ApiController]
[Route("api/ai/strategy")]
public class AiStrategyController(IAiStrategyPipelineService pipeline) : ControllerBase
{
    private static readonly ConcurrentDictionary<string, List<long>> RateWindow = new();
    private const int MaxPerMinute = 12;

    private (string? Ip, string Op) AuditCtx()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var op = Request.Headers["X-Operator"].ToString().Trim();
        return (ip, string.IsNullOrEmpty(op) ? "web-client" : op);
    }

    private IActionResult? RefuseIfRateLimited()
    {
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var list = RateWindow.GetOrAdd(key, _ => new List<long>());
        lock (list)
        {
            list.RemoveAll(t => now - t > 60);
            if (list.Count >= MaxPerMinute)
                return StatusCode(429, new { error = "AI 请求过于频繁，请稍后再试（每分钟最多 12 次）。" });
            list.Add(now);
        }
        return null;
    }

    /// <summary>自然语言 → 白名单策略建议（不跑回测）。</summary>
    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest([FromBody] AiSuggestRequest request, CancellationToken ct)
    {
        if (RefuseIfRateLimited() is { } denied) return denied;
        try { return Ok(await pipeline.SuggestAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>建议并立即回测（推荐主入口）。</summary>
    [HttpPost("suggest-and-run")]
    public async Task<IActionResult> SuggestAndRun([FromBody] AiSuggestAndRunRequest request, CancellationToken ct)
    {
        if (RefuseIfRateLimited() is { } denied) return denied;
        try { return Ok(await pipeline.SuggestAndRunAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(502, new { error = ex.Message }); }
    }

    /// <summary>部署前检查清单（不落库、不改配置）。</summary>
    [HttpPost("deploy-preflight")]
    public IActionResult DeployPreflight([FromBody] DeployPreflightRequest request)
    {
        try { return Ok(pipeline.PreflightDeploy(request)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>将策略参数部署到量化引擎，强制 DryRun（绝不会从本接口开启真实下单）。</summary>
    [HttpPost("deploy-dry-run")]
    public IActionResult DeployDryRun([FromBody] DeployDryRunRequest request)
    {
        var (ip, op) = AuditCtx();
        try { return Ok(pipeline.DeployDryRun(request, op, ip)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
