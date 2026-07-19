using IBTrade.Application.IBKR;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// IB Gateway 实时接入（路由仍为 /api/tws，兼容旧客户端）：
/// - POST /api/tws/sync      手动触发一次快照合并入库（市价刷新，不覆盖 Flex 仓位结构）
/// - POST /api/tws/live/start 启动持久实时推送
/// - POST /api/tws/live/stop  停止实时推送
/// - GET  /api/tws/live       读取当前实时快照
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TwsController(ITwsSyncService sync, TwsLiveFeed live) : ControllerBase
{
    /// <summary>连接 IB Gateway 拉取实时快照并合并入库（Flex 数量/均价保留）。</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        try
        {
            var snap = await sync.SyncAsync(ct);
            return Ok(new
            {
                accountId = snap.AccountId,
                currency = snap.Currency,
                netLiquidation = snap.NetLiquidation,
                cashBalance = snap.CashBalance,
                positionCount = snap.Positions.Count,
                connectedPort = snap.ConnectedPort,
                source = snap.Source is "TWS" or "Gateway" ? snap.Source : "Gateway",
                note = "仅刷新市价；数量/均价以 Flex 为准（若该账户已有 Flex 仓位）"
            });
        }
        catch (Exception ex)
        {
            var msg = ex is InvalidOperationException
                ? ex.Message
                : $"Gateway 同步失败：{ex.Message}";
            return new ContentResult
            {
                Content = msg,
                ContentType = "text/plain; charset=utf-8",
                StatusCode = 502
            };
        }
    }

    /// <summary>启动持久实时推送（连接保持不关闭，价格一动即刷新）。</summary>
    [HttpPost("live/start")]
    public async Task<IActionResult> LiveStart(CancellationToken ct)
    {
        try
        {
            var snap = await live.StartAsync(ct);
            return Ok(new
            {
                running = true,
                accountId = snap.AccountId,
                source = snap.Source,
                connectedPort = snap.ConnectedPort,
                positionCount = snap.Positions.Count
            });
        }
        catch (Exception ex)
        {
            var msg = ex is InvalidOperationException ? ex.Message : $"实时推送启动失败：{ex.Message}";
            return new ContentResult
            {
                Content = msg,
                ContentType = "text/plain; charset=utf-8",
                StatusCode = 502
            };
        }
    }

    /// <summary>停止实时推送。</summary>
    [HttpPost("live/stop")]
    public IActionResult LiveStop()
    {
        live.Stop();
        return Ok(new { running = false });
    }

    /// <summary>读取当前实时快照（前端高频轮询）。未启动或已断开返回 running=false。</summary>
    [HttpGet("live")]
    public IActionResult Live()
    {
        var snap = live.GetSnapshot();
        if (snap is null)
            return Ok(new { running = false, lastError = live.LastError });

        return Ok(new
        {
            running = true,
            accountId = snap.AccountId,
            currency = snap.Currency,
            source = snap.Source,
            connectedPort = snap.ConnectedPort,
            lastTickUtc = snap.LastTickUtc,
            netLiquidation = snap.NetLiquidation,
            cashBalance = snap.CashBalance,
            positions = snap.Positions.Select(p => new
            {
                symbol = p.Symbol,
                assetType = p.AssetType.ToString(),
                currency = p.Currency,
                quantity = p.Quantity,
                avgCost = p.AvgCost,
                lastPrice = p.LastPrice,
                bid = p.Bid,
                ask = p.Ask,
                marketValue = p.MarketValue,
                unrealizedPnl = p.UnrealizedPnl,
                change = p.Change
            })
        });
    }
}
