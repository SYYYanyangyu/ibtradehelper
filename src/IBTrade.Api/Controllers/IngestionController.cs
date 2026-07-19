using IBTrade.Application.Ingestion;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// Flex 数据接入：手动触发报表摄入，并查询各通道最近成功状态。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IngestionController(IIngestionService ingestion, IDataFeedStatusRepository feedStatus) : ControllerBase
{
    /// <summary>
    /// 拉取并入库。
    /// env：live / paper / 省略(=全部已配置环境各跑一次)。
    /// queryId：可选；省略时使用该环境默认 queryId。
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromQuery] TradeEnv? env, [FromQuery] string? queryId, CancellationToken ct)
    {
        try
        {
            if (env is null)
                await ingestion.IngestAllAsync(ct);
            else
                await ingestion.IngestAsync(env.Value, queryId, ct);
        }
        catch (Exception ex)
        {
            // 避免把 SSL/代理堆栈直接甩给浏览器；前端只展示可读文案
            var msg = ex is InvalidOperationException or ArgumentException
                ? ex.Message
                : $"Flex 摄入失败：{ex.GetBaseException().Message}";
            return StatusCode(StatusCodes.Status502BadGateway, new { error = msg });
        }

        var statuses = await feedStatus.GetAllAsync(ct);
        return Ok(new
        {
            status = "ingested",
            env = env?.ToString() ?? "all",
            queryId,
            feeds = statuses.Select(s => new
            {
                s.Feed,
                s.Environment,
                s.LastSuccessUtc,
                s.AccountId,
                s.ConnectedPort,
                s.Message
            })
        });
    }

    /// <summary>各数据通道最近一次成功同步时间（Flex / Gateway）。</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var statuses = await feedStatus.GetAllAsync(ct);
        return Ok(new
        {
            feeds = statuses.Select(s => new
            {
                s.Feed,
                s.Environment,
                s.LastSuccessUtc,
                s.AccountId,
                s.ConnectedPort,
                s.Message
            }),
            legend = new
            {
                flex = "成交 / 日净值 / 持仓数量与均价（Token+QueryId 报表）",
                gateway = "实时市价刷新 / 下单 / K 线（本机 IB Gateway）",
                estimate = "盘中由持仓推算的净值（非官方）"
            }
        });
    }
}
