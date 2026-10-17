using System.Collections.Concurrent;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>期权情报独立模块：期权链 + 财报 + 新闻 + 美联储 + 预测市场 + AI 解读。</summary>
[ApiController]
[Route("api/options")]
public class OptionsIntelController(IOptionsIntelService svc) : ControllerBase
{
    private static readonly ConcurrentDictionary<string, List<long>> RateWindow = new();

    [HttpGet("hot")]
    public async Task<IActionResult> Hot([FromQuery] string? symbol, CancellationToken ct)
    {
        try { return Ok(await svc.GetHotAsync(symbol, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(502, new { error = "热门榜拉取失败：" + ex.GetBaseException().Message }); }
    }

    [HttpGet("context")]
    public async Task<IActionResult> Context([FromQuery] string symbol, [FromQuery] string? expiry, CancellationToken ct)
    {
        try { return Ok(await svc.GetContextAsync(symbol, expiry, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(502, new { error = "期权情报拉取失败：" + ex.GetBaseException().Message }); }
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] OptionsAnalyzeRequest request, CancellationToken ct)
    {
        if (RefuseIfRateLimited() is { } denied) return denied;
        try { return Ok(await svc.AnalyzeAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(502, new { error = "AI 分析失败：" + ex.GetBaseException().Message }); }
    }

    private IActionResult? RefuseIfRateLimited()
    {
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var list = RateWindow.GetOrAdd(key, _ => new List<long>());
        lock (list)
        {
            list.RemoveAll(t => now - t > 60);
            if (list.Count >= 8)
                return StatusCode(429, new { error = "期权 AI 请求过于频繁，请稍后再试（每分钟最多 8 次）。" });
            list.Add(now);
        }
        return null;
    }
}
