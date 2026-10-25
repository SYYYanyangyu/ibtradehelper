using IBTrade.Application.Discipline;
using IBTrade.Application.Orders.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

[ApiController]
[Route("api/discipline")]
public class DisciplineController(IDisciplineGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await gate.GetStatusAsync(ct));

    [HttpPut("settings")]
    public async Task<IActionResult> Settings([FromBody] DisciplineSettings settings, CancellationToken ct)
    {
        try
        {
            gate.UpdateSettings(settings);
            return Ok(await gate.GetStatusAsync(ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>下单前预检（不下单）。</summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] PlaceOrderRequest req, CancellationToken ct) =>
        Ok(await gate.CheckAsync(req, ct));
}
