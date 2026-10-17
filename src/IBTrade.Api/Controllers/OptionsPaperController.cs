using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.OptionsPaper;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

[ApiController]
[Route("api/options/paper")]
public class OptionsPaperController(IOptionsPaperEngine engine, IOptionsIntelService intel) : ControllerBase
{
    [HttpGet]
    public IActionResult Status() => Ok(engine.GetStatus());

    [HttpPut("settings")]
    public async Task<IActionResult> Settings([FromBody] OptionsPaperSettings settings, CancellationToken ct)
    {
        try
        {
            if (await RefuseIfCold(settings.Symbol, ct) is { } denied) return denied;
            engine.UpdateSettings(settings);
            return Ok(engine.GetStatus());
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        try
        {
            if (await RefuseIfCold(engine.GetStatus().Settings.Symbol, ct) is { } denied) return denied;
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
        try
        {
            await engine.TickAsync(ct);
            return Ok(engine.GetStatus());
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return StatusCode(502, new { error = ex.GetBaseException().Message }); }
    }

    private async Task<IActionResult?> RefuseIfCold(string symbol, CancellationToken ct)
    {
        var hot = await intel.GetHotAsync(symbol, ct);
        if (hot.Names.Count == 0) return null;
        if (hot.CurrentIsHot) return null;
        return BadRequest(new
        {
            error = $"{symbol} 不在 Nasdaq 今日成交额活跃榜，Paper 只做热门标的。请点榜上的代码。"
        });
    }
}
