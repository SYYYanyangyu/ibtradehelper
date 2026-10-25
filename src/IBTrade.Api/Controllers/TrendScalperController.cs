using IBTrade.Application.Quant.TrendScalper;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

[ApiController]
[Route("api/trend-scalper")]
public class TrendScalperController(ITrendScalperEngine engine) : ControllerBase
{
    [HttpGet]
    public IActionResult Status() => Ok(engine.GetStatus());

    [HttpPut("settings")]
    public IActionResult Settings([FromBody] TrendScalperSettings settings)
    {
        try
        {
            engine.UpdateSettings(settings);
            return Ok(engine.GetStatus());
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("start")]
    public IActionResult Start()
    {
        try
        {
            engine.Start();
            return Ok(engine.GetStatus());
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        engine.Stop();
        return Ok(engine.GetStatus());
    }

    [HttpPost("tick")]
    public async Task<IActionResult> Tick(CancellationToken ct)
    {
        await engine.TickAsync(ct);
        return Ok(engine.GetStatus());
    }

    [HttpPost("flatten")]
    public async Task<IActionResult> Flatten(CancellationToken ct)
    {
        try
        {
            await engine.FlattenAsync(ct);
            return Ok(engine.GetStatus());
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(502, new { error = ex.GetBaseException().Message }); }
    }
}
