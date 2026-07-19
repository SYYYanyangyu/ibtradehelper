using IBTrade.Application.AutoTrade;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// 下单执行接口（独立于看板）：下单 / 撤单 / 查询，以及风控配置。
/// 默认走 paper 模拟盘，受 RiskGuard 拦截；实盘需显式在风控配置中开启且理解风险。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrdersController(
    IOrderService svc,
    IOrderReconcileService reconcile,
    IAutoTrader trader,
    IHttpContextAccessor http) : ControllerBase
{
    private (string? Ip, string Op) AuditCtx()
    {
        var ctx = http.HttpContext;
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var op = ctx?.Request.Headers["X-Operator"].ToString().Trim();
        return (ip, string.IsNullOrEmpty(op) ? "web-client" : op);
    }

    /// <summary>下限价/市价单。风控拦截或 Gateway 未连会返回 4xx。</summary>
    [HttpPost]
    public async Task<IActionResult> Place([FromBody] PlaceOrderRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Symbol))
            return BadRequest(new { error = "symbol 必填" });

        var (ip, op) = AuditCtx();
        var symbol = req.Symbol.Trim().ToUpperInvariant();
        try
        {
            // 限价单用限价；市价单用调用方传入的估算价（前端最新行情），缺失时由风控以失败-关闭方式拒绝。
            var dto = await svc.PlaceAsync(req, ct, req.Type == OrderType.Limit ? req.LimitPrice : req.EstimatedPrice);
            trader.RecordAudit(AutoTradeEventType.OrderPlaced,
                $"手动下单 {req.Side} {req.Quantity} {symbol} (type={req.Type}) → {dto.Status}", ip, op);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            trader.RecordAudit(AutoTradeEventType.RiskBlocked,
                $"手动下单被风控拦截 {req.Side} {req.Quantity} {symbol}: {ex.Message}", ip, op);
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            trader.RecordAudit(AutoTradeEventType.OrderRejected,
                $"手动下单被拒 {req.Side} {req.Quantity} {symbol}: {ex.Message}", ip, op);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>撤单。</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct)
    {
        var (ip, op) = AuditCtx();
        var ok = await svc.CancelAsync(id, ct);
        trader.RecordAudit(AutoTradeEventType.Info, $"手动撤单 {id} → {(ok ? "成功" : "失败/不存在")}", ip, op);
        return ok ? Ok(new { cancelled = true }) : NotFound(new { error = "订单不存在或未连接 Gateway" });
    }

    /// <summary>列出全部订单（状态为实时合并）。</summary>
    [HttpGet]
    public IActionResult List() => Ok(svc.List());

    /// <summary>查询单笔订单。</summary>
    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var o = svc.Get(id);
        return o is null ? NotFound(new { error = "订单不存在" }) : Ok(o);
    }

    /// <summary>
    /// 订单对账：本地成交净额 vs DB 持仓（只读，不改仓）。
    /// 持仓仍以 Flex/TWS 同步为权威；差额用于发现未落库/外部成交。
    /// </summary>
    [HttpGet("reconcile")]
    public async Task<IActionResult> Reconcile(CancellationToken ct)
    {
        try { return Ok(await reconcile.ReconcileAsync(ct)); }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    /// <summary>读取风控配置。</summary>
    [HttpGet("risk-settings")]
    public IActionResult Risk() => Ok(svc.Risk.Settings);

    /// <summary>更新风控配置（关闭总开关 / 设模拟盘 / 调上限等）。</summary>
    [HttpPost("risk-settings")]
    public IActionResult UpdateRisk([FromBody] RiskSettings settings)
    {
        if (settings is null) return BadRequest(new { error = "缺少配置" });
        svc.Risk.Update(settings);
        return Ok(svc.Risk.Settings);
    }
}
