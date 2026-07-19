using IBTrade.Application.AutoTrade;
using IBTrade.Application.Orders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>自动交易引擎 HTTP 接口：配置订阅、启停、状态、审计日志、手动评估。</summary>
[ApiController]
[Route("api/autotrade")]
public class AutoTradeController : ControllerBase
{
    private readonly IAutoTrader _trader;
    private readonly IHttpContextAccessor _http;

    public AutoTradeController(IAutoTrader trader, IHttpContextAccessor http)
    {
        _trader = trader;
        _http = http;
    }

    /// <summary>从请求中提取审计上下文：来源 IP + 操作人（X-Operator 头，缺省 web-client）。</summary>
    private (string? Ip, string Op) AuditCtx()
    {
        var ctx = _http.HttpContext;
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var op = ctx?.Request.Headers["X-Operator"].ToString().Trim();
        return (ip, string.IsNullOrEmpty(op) ? "web-client" : op);
    }

    /// <summary>引擎总览：运行状态、全局设置、风控、各标的配置+运行时、最近日志。</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        var risk = _trader.Risk.Settings;
        var configs = _trader.GetConfigs();
        var runtime = _trader.GetRuntime();
        var events = _trader.GetEvents(50);

        var symbols = configs.Select(c =>
        {
            var rt = runtime.FirstOrDefault(r => string.Equals(r.Symbol, c.Symbol, StringComparison.OrdinalIgnoreCase));
            return new
            {
                config = c,
                runtime = rt ?? new AutoTradeSymbolRuntime { Symbol = c.Symbol, StrategyKey = c.StrategyKey }
            };
        }).ToList();

        return Ok(new
        {
            running = _trader.IsRunning,
            settings = _trader.Settings,
            risk = new
            {
                enabled = risk.Enabled,
                mode = risk.Mode,
                maxOpenPositions = risk.MaxOpenPositions,
                maxOrderAmount = risk.MaxOrderAmount,
                dailyLossLimit = risk.DailyLossLimit
            },
            symbols,
            events
        });
    }

    /// <summary>列出所有订阅配置。</summary>
    [HttpGet("configs")]
    public IActionResult Configs() => Ok(_trader.GetConfigs());

    /// <summary>新增或更新一个标的的订阅配置（按 Symbol 幂等）。</summary>
    [HttpPost("config")]
    public IActionResult AddConfig([FromBody] AutoTradeSymbolConfig cfg)
    {
        var (ip, op) = AuditCtx();
        try { _trader.UpsertConfig(cfg, op, ip); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        return Ok(cfg);
    }

    /// <summary>移除某个标的的订阅。</summary>
    [HttpDelete("config/{symbol}")]
    public IActionResult RemoveConfig(string symbol)
    {
        var (ip, op) = AuditCtx();
        _trader.RemoveConfig(symbol, op, ip);
        return Ok(new { removed = symbol });
    }

    /// <summary>更新全局设置（Enabled / PollIntervalSeconds）。</summary>
    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] AutoTradeSettings s)
    {
        var (ip, op) = AuditCtx();
        _trader.UpdateSettings(s, op, ip);
        return Ok(_trader.Settings);
    }

    /// <summary>启动后台轮询循环（总开关关闭时循环空转，不评估）。</summary>
    [HttpPost("start")]
    public IActionResult Start()
    {
        var (ip, op) = AuditCtx();
        _trader.Start(op, ip);
        return Ok(new { running = _trader.IsRunning });
    }

    /// <summary>停止后台轮询循环。</summary>
    [HttpPost("stop")]
    public IActionResult Stop()
    {
        var (ip, op) = AuditCtx();
        _trader.Stop(op, ip);
        return Ok(new { running = _trader.IsRunning });
    }

    /// <summary>立即对单个标的评估一次（便于验证信号；是否真实下单取决于该标的 DryRun）。</summary>
    [HttpPost("evaluate/{symbol}")]
    public async Task<IActionResult> Evaluate(string symbol)
    {
        var (ip, op) = AuditCtx();
        try { await _trader.EvaluateNowAsync(symbol, ct: default, operatorName: op, ip: ip); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        return Ok(new { evaluated = symbol });
    }

    /// <summary>最近审计日志。</summary>
    [HttpGet("events")]
    public IActionResult Events([FromQuery] int max = 50) => Ok(_trader.GetEvents(max));
}
