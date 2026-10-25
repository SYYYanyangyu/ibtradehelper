using IBTrade.Application.Portfolio;
using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace IBTrade.Api.Controllers;

/// <summary>
/// 组合看板接口：返回盈亏汇总、每日盈亏曲线、现金流录入，并支持手动生成每日净值快照。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PortfolioController(
    IPortfolioService portfolioService,
    ISnapshotService snapshotService,
    IDailyPnlService dailyPnlService,
    IPerformanceCompareService performanceCompare,
    IPositionDailyPnlService positionDailyPnlService,
    ICashTransactionRepository cashTxnRepo,
    IRealizedPnlService realizedPnlService,
    ITradePnlService tradePnlService,
    IWeeklyReviewService weeklyReview,
    ITradeRepository tradeRepo,
    IAccountRepository accountRepo) : ControllerBase
{
    private const int MaxDailyPnlDays = 365;
    private const int MaxTradesDays = 3650;
    private const int DefaultTradesDays = 365;
    private const int DefaultDailyPnlDays = 30;

    /// <summary>
    /// 获取组合盈亏看板。
    /// 不传 accountId 汇总所有账户；传值则只看该账户。
    /// env：live / paper 可过滤真实或模拟账户；省略则全部。
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<PortfolioSummaryDto>> GetSummary(
        [FromQuery] string? accountId,
        [FromQuery] TradeEnv? env,
        CancellationToken ct)
    {
        var summary = await portfolioService.GetSummaryAsync(accountId, env, ct);
        return Ok(summary);
    }

    /// <summary>
    /// 按标的历史盈亏（已实现/未实现）：数据来自 IBKR Flex 报表落库的 RealizedPnls 表。
    /// 不传 accountId 汇总所有账户；传值则只看该账户。逐标的按已实现盈亏降序。
    /// env：live / paper 可过滤真实或模拟账户；省略则全部。
    /// </summary>
    [HttpGet("realized")]
    public async Task<ActionResult<RealizedPnlSummaryDto>> GetRealized(
        [FromQuery] string? accountId,
        [FromQuery] TradeEnv? env,
        CancellationToken ct)
    {
        var result = await realizedPnlService.GetAsync(accountId, env, ct);
        return Ok(result);
    }

    /// <summary>逐笔 FIFO 平仓盈亏。每一例是一笔开仓被反向成交平掉的部分。</summary>
    [HttpGet("trade-pnl")]
    public async Task<ActionResult<TradePnlDto>> GetTradePnl(
        [FromQuery] string? accountId,
        [FromQuery] TradeEnv? env,
        CancellationToken ct)
    {
        var result = await tradePnlService.GetAsync(accountId, env, ct);
        return Ok(result);
    }

    /// <summary>每周复盘：胜率 / 期望 / 回撤，逐条对照纪律设置统计违规次数与对应盈亏。</summary>
    [HttpGet("weekly-review")]
    public async Task<ActionResult<WeeklyReviewDto>> GetWeeklyReview(
        [FromQuery] string? accountId,
        [FromQuery] TradeEnv? env,
        [FromQuery] int weeks = 8,
        CancellationToken ct = default)
    {
        var result = await weeklyReview.GetAsync(accountId, env, weeks, ct);
        return Ok(result);
    }

    /// <summary>
    /// 每日盈亏曲线：最近 days 天（默认 30，上限 365）的每日净值与纯交易盈亏。
    /// 不传 accountId 取跨账户汇总（"ALL"）。
    /// </summary>
    [HttpGet("daily-pnl")]
    public async Task<IActionResult> GetDailyPnl(
        [FromQuery] string? accountId,
        [FromQuery] int days = DefaultDailyPnlDays,
        [FromQuery] TradeEnv? env = null,
        CancellationToken ct = default)
    {
        if (days is < 1 or > MaxDailyPnlDays) days = DefaultDailyPnlDays;
        var result = await dailyPnlService.GetAsync(accountId, days, env, ct);
        return Ok(result);
    }

    /// <summary>
    /// 组合收益曲线 vs 恒生 / 标普500 / 道琼斯 / 纳斯达克。
    /// 组合用 Flex 官方净值；指数用 IB Gateway IND 日线。区间起点归一为 0。
    /// range：1M / 3M / 6M / YTD / 1Y / ALL
    /// </summary>
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance(
        [FromQuery] string? accountId,
        [FromQuery] string range = "1Y",
        [FromQuery] TradeEnv? env = null,
        CancellationToken ct = default)
    {
        var result = await performanceCompare.GetAsync(accountId, range, env, ct);
        return Ok(result);
    }

    /// <summary>
    /// 个股每日盈亏归因：取指定日期（默认今天）每只标的的当日贡献排行。
    /// 贡献 = 当日市值 − 前一日市值；便于回答"今天赚/亏主要是哪只股票贡献的"。
    /// env：live / paper 过滤；与 accountId 同时省略时用历史 ALL（混盘，不推荐）。
    /// </summary>
    [HttpGet("position-daily-pnl")]
    public async Task<IActionResult> GetPositionDailyPnl(
        [FromQuery] string? accountId,
        [FromQuery] string? date,
        [FromQuery] TradeEnv? env = null,
        CancellationToken ct = default)
    {
        var day = DateTime.TryParse(date, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var result = await positionDailyPnlService.GetByDateAsync(accountId, day, env, ct);
        return Ok(result);
    }

    /// <summary>
    /// 手动生成净值快照（默认"今天"，也可由后台服务在交易日收盘后自动触发）。
    /// cashBalance 为可选现金余额（当前持仓数据不含现金，可留空）。
    /// date 为可选日期（yyyy-MM-dd，按 UTC 解释），用于回填历史某天的快照，
    /// 例如补拍"昨日"基准后即可算出真实的今日盈亏（无需编造数据）。
    /// </summary>
    [HttpPost("snapshot")]
    public async Task<IActionResult> CaptureSnapshot(
        [FromQuery] string? accountId,
        [FromQuery] decimal? cashBalance,
        [FromQuery] string? date,
        CancellationToken ct)
    {
        DateTime? asOf = null;
        if (!string.IsNullOrWhiteSpace(date)
            && DateTime.TryParse(date, out var parsed))
        {
            asOf = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        }

        var capturedDate = await snapshotService.CaptureTodayAsync(accountId, cashBalance, asOf, ct);
        return Ok(new
        {
            status = "snapshot-captured",
            accountId = accountId ?? "ALL",
            date = capturedDate.ToString("yyyy-MM-dd")
        });
    }

    /// <summary>
    /// 记录一笔外部现金流（入金/出金/股息/利息/费用）。
    /// 金额符号会按类型自动归一（入金/股息/利息为正，出金/费用为负）；
    /// 用途：从净值差分中扣除，得到纯交易盈亏。默认计入今天，可用 date 指定历史日期。
    /// </summary>
    [HttpPost("cash-transactions")]
    public async Task<IActionResult> AddCashTransaction([FromBody] CashTransactionRequest req, CancellationToken ct)
    {
        var accountId = string.IsNullOrWhiteSpace(req.AccountId) ? "ALL" : req.AccountId;
        var date = DateTime.TryParse(req.Date, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc)
            : DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

        var txn = new CashTransaction
        {
            AccountId = accountId,
            DateUtc = date,
            Type = req.Type,
            Amount = NormalizeAmount(req.Type, req.Amount),
            Currency = req.Currency ?? "USD",
            Note = req.Note
        };

        await cashTxnRepo.AddAsync(txn, ct);
        await cashTxnRepo.SaveChangesAsync(ct);
        return Ok(txn);
    }

    /// <summary>
    /// 查询某账户最近 days 天的外部现金流（默认 30，上限 365）。
    /// </summary>
    [HttpGet("cash-transactions")]
    public async Task<IActionResult> GetCashTransactions(
        [FromQuery] string? accountId,
        [FromQuery] int days = DefaultDailyPnlDays,
        CancellationToken ct = default)
    {
        if (days is < 1 or > MaxDailyPnlDays) days = DefaultDailyPnlDays;
        var key = string.IsNullOrWhiteSpace(accountId) ? "ALL" : accountId;
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var list = await cashTxnRepo.GetSinceAsync(key, since, ct);
        return Ok(list);
    }

    /// <summary>账户列表（含 Paper/Live 环境），供前端按环境切换。</summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts(CancellationToken ct)
    {
        var list = await accountRepo.GetAllAsync(ct);
        return Ok(list.Select(a => new
        {
            accountId = a.AccountId,
            environment = a.Environment.ToString(),
            alias = a.Alias,
            baseCurrency = a.BaseCurrency
        }));
    }

    /// <summary>
    /// 成交明细：来自 IBKR Flex 报表落库的 Trades 表（每笔已发生的买/卖）。
    /// 返回最近 days 天（默认 365，上限 3650）的成交；
    /// accountId / env 可过滤（env=paper|live，省略则全部——会混真实与模拟，看板默认应传 paper）。
    /// </summary>
    [HttpGet("trades")]
    public async Task<IActionResult> GetTrades(
        [FromQuery] string? accountId,
        [FromQuery] TradeEnv? env,
        [FromQuery] int days = DefaultTradesDays,
        CancellationToken ct = default)
    {
        if (days is < 1 or > MaxTradesDays) days = DefaultTradesDays;
        var since = DateTime.UtcNow.AddDays(-days);
        var key = string.IsNullOrWhiteSpace(accountId) || accountId == "ALL" ? null : accountId;

        HashSet<string>? allowed = null;
        if (!string.IsNullOrWhiteSpace(key) || env is not null)
        {
            var accounts = await accountRepo.GetAllAsync(ct);
            if (!string.IsNullOrWhiteSpace(key))
            {
                if (env is not null)
                {
                    var acc = accounts.FirstOrDefault(a => a.AccountId == key);
                    allowed = acc is not null && acc.Environment == env
                        ? new HashSet<string> { key } : new HashSet<string>();
                }
                else
                {
                    allowed = new HashSet<string> { key };
                }
            }
            else
            {
                allowed = accounts.Where(a => a.Environment == env).Select(a => a.AccountId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        var list = await tradeRepo.GetAllAsync(ct);
        var filtered = list
            .Where(x => x.TradeTimeUtc >= since)
            .Where(x => allowed is null || allowed.Contains(x.AccountId))
            .OrderBy(x => x.TradeTimeUtc)
            .ToList();
        return Ok(filtered);
    }

    /// <summary>按现金流类型归一化金额符号：流入为正、流出为负，Other 保持原值。</summary>
    private static decimal NormalizeAmount(CashFlowType type, decimal amount) => type switch
    {
        CashFlowType.Deposit or CashFlowType.Dividend or CashFlowType.Interest => Math.Abs(amount),
        CashFlowType.Withdrawal or CashFlowType.Fee => -Math.Abs(amount),
        _ => amount
    };
}

/// <summary>录入现金流的请求体。</summary>
public record CashTransactionRequest(
    string? AccountId,
    CashFlowType Type,
    decimal Amount,
    string? Currency,
    string? Date,
    string? Note);
