using IBTrade.Application.MarketData;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// 行情数据接口（独立于看板）：拉取历史 K 线，供回测/策略使用。
/// 示例：GET /api/marketdata/history?symbol=AAPL&amp;duration=1 Y&amp;barSize=1 day
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MarketDataController(IHistoricalDataService svc) : ControllerBase
{
    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] string symbol,
        [FromQuery] string? exchange,
        [FromQuery] string? currency,
        [FromQuery] string duration = "1 Y",
        [FromQuery] string barSize = "1 day",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { error = "symbol 必填，例如 ?symbol=AAPL" });

        var bars = await svc.GetBarsAsync(symbol, exchange, currency, duration, barSize, ct);
        return Ok(new
        {
            symbol,
            exchange = exchange ?? "SMART",
            currency = currency ?? "USD",
            duration,
            barSize,
            count = bars.Count,
            bars
        });
    }
}
