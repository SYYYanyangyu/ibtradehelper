using IBTrade.Application.Positions;
using IBTrade.Application.Positions.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// 持仓接口：表现层只负责 HTTP 语义，业务委托给应用层服务。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PositionsController(IPositionService positionService) : ControllerBase
{
    /// <summary>查询持仓（可按账户过滤）。</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> GetPositions(
        [FromQuery] string? accountId,
        CancellationToken ct)
    {
        var result = await positionService.GetPositionsAsync(accountId, ct);
        return Ok(result);
    }
}
