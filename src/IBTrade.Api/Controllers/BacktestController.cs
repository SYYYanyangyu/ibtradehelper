using IBTrade.Application.Backtest;
using IBTrade.Application.Backtest.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// 回测接口（独立于看板）：拉历史数据 → 跑策略 → 输出绩效指标与资金曲线。
/// 示例：POST /api/backtest/run  { "symbol":"AAPL","strategy":"sma_cross","parameters":{"fast":10,"slow":30} }
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BacktestController(IBacktestService svc) : ControllerBase
{
    /// <summary>运行一次回测。</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] BacktestRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest(new { error = "symbol 必填，例如 { \"symbol\": \"AAPL\" }" });

        try
        {
            var result = await svc.RunAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    /// <summary>列出可用策略（含参数元数据，供前端动态渲染参数输入框）。</summary>
    [HttpGet("strategies")]
    public IActionResult Strategies()
        => Ok(svc.ListStrategies().Select(s => new
        {
            key = s.Key,
            name = s.Name,
            parameters = s.Parameters.Select(p => new
            {
                key = p.Key,
                label = p.Label,
                defaultValue = p.Default,
                step = p.Step,
                min = p.Min,
                max = p.Max
            })
        }));
}
