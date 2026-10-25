using IBTrade.Application.MarketData;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

[ApiController]
[Route("api/market-news")]
public class MarketNewsController(MarketNewsService news) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string symbol = "QQQ", CancellationToken ct = default) =>
        Ok(await news.GetAsync(symbol, ct));
}
