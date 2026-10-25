using IBTrade.Application.IBKR;
using IBTrade.Application.Ingestion.Dtos;
using IBTrade.Application.Portfolio;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.Ingestion;

/// <summary>
/// 入库用例实现：拉取 Flex 报表 → 解析 → 写入数据库。
/// 支持 Live（真实）与 Paper（模拟）双环境。
/// 配置来源优先级：环境变量（IBKR_FLEX_*_LIVE/PAPER）优先，其次 appsettings.json 的 IBKR:Flex 段。
/// </summary>
public class IngestionService(
    IFlexQueryClient flexClient,
    IAccountRepository accountRepo,
    IPositionRepository positionRepo,
    ITradeRepository tradeRepo,
    ICashTransactionRepository cashTxnRepo,
    IRealizedPnlRepository realizedPnlRepo,
    IDailySnapshotRepository snapshotRepo,
    IPositionDailySnapshotRepository positionSnapshotRepo,
    ISnapshotService snapshotService,
    IDataFeedStatusRepository feedStatusRepo,
    IConfiguration configuration,
    ILogger<IngestionService>? logger = null) : IIngestionService
{
    public async Task IngestAsync(TradeEnv env, string? queryId = null, CancellationToken ct = default)
    {
        var (token, baseUrl, defaultQid) = ResolveEnv(env);
        var qid = queryId ?? defaultQid
                  ?? throw new InvalidOperationException(
                      $"未提供 queryId，且环境变量 IBKR_FLEX_QUERYID_{(env == TradeEnv.Paper ? "PAPER" : "LIVE")} 未设置。");

        var xml = await flexClient.DownloadXmlAsync(token, qid, baseUrl, ct);
        var snap = flexClient.Parse(xml);

        // 1) 账户：逐个 upsert，并打上环境标签（多账户报表会一次返回多个账户）
        foreach (var a in snap.Accounts)
        {
            var existing = await accountRepo.GetByAccountIdAsync(a.AccountId, ct);
            if (existing is null)
            {
                await accountRepo.AddAsync(new Account
                {
                    AccountId = a.AccountId,
                    BaseCurrency = a.BaseCurrency,
                    Alias = a.Alias,
                    Environment = env
                }, ct);
            }
            else
            {
                existing.BaseCurrency = a.BaseCurrency;
                existing.Alias = a.Alias;
                existing.Environment = env;
            }
        }
        await accountRepo.SaveChangesAsync(ct);

        // 2) 持仓：快照型数据，按账户幂等替换。
        //    必须覆盖报表内全部账户（含空仓），否则旧 OpenPosition 残留会把「空仓」显示成数万市值。
        //    但若 Flex 报表未带 Open Positions 段（positions=0），禁止清空已有持仓——否则会把 Gateway 结构清掉。
        var positionAccountIds = snap.Accounts.Select(a => a.AccountId)
            .Concat(snap.Positions.Select(p => p.AccountId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var flexHasAnyPositions = snap.Positions.Any(p => p.Quantity != 0m);
        foreach (var acctId in positionAccountIds)
        {
            var rows = snap.Positions
                .Where(p => string.Equals(p.AccountId, acctId, StringComparison.OrdinalIgnoreCase)
                            && p.Quantity != 0m)
                .Select(p => new Position
                {
                    AccountId = p.AccountId,
                    Symbol = p.Symbol,
                    AssetType = p.AssetType,
                    Currency = p.Currency,
                    Quantity = p.Quantity,
                    AvgCost = p.AvgCost,
                    MarketPrice = p.MarketPrice,
                    Multiplier = p.Multiplier > 0 ? p.Multiplier : 1m,
                    Source = DataSources.Flex,
                    PriceSource = DataSources.Flex
                }).ToList();
            if (rows.Count == 0 && !flexHasAnyPositions)
                continue;
            await positionRepo.ReplaceByAccountAsync(acctId, rows, ct);
        }
        await positionRepo.SaveChangesAsync(ct);

        // 3) 成交：增量写入，但按复合键去重，避免重复摄入同一报表产生重复行
        //    （Flex 报表为滚动窗口，重复摄入后历史成交会与既有记录重复，放大市值/盈亏/FIFO 已实现盈亏）
        foreach (var acctId in snap.Trades.Select(t => t.AccountId).Distinct())
        {
            var incoming = snap.Trades.Where(t => t.AccountId == acctId).ToList();
            var existing = await tradeRepo.GetByAccountAsync(acctId, ct);
            var seen = new HashSet<string>(existing.Select(TradeSignature));
            var toAdd = incoming
                .Where(t => !seen.Contains(TradeSignature(t)))
                .Select(t => new Trade
                {
                    AccountId = t.AccountId,
                    Symbol = t.Symbol,
                    AssetType = t.AssetType,
                    Side = t.Side,
                    Currency = t.Currency,
                    Quantity = t.Quantity,
                    Price = t.Price,
                    Commission = t.Commission,
                    Multiplier = t.Multiplier > 0
                        ? t.Multiplier
                        : (t.AssetType == AssetType.Option ? 100m : 1m),
                    TradeTimeUtc = t.TradeTimeUtc
                }).ToList();
            if (toAdd.Count > 0)
            {
                await tradeRepo.AddRangeAsync(toAdd, ct);
                await tradeRepo.SaveChangesAsync(ct);
            }
        }

        // 4) 外部现金流：按 (账户,日期,类型,金额,币种,备注) 签名合并去重，
        //    仅删除与本次报表签名重合的记录并重插，保留手动录入 / 窗口外的现金流不被整账户清空（C-5）。
        if (snap.CashTransactionsOrEmpty.Any())
        {
            foreach (var acctId in snap.CashTransactionsOrEmpty.Select(c => c.AccountId).Distinct())
            {
                var incoming = snap.CashTransactionsOrEmpty
                    .Where(c => c.AccountId == acctId)
                    .Select(c => new CashTransaction
                    {
                        AccountId = c.AccountId,
                        DateUtc = c.DateUtc.Date,
                        Type = c.Type,
                        Amount = c.Amount,
                        Currency = c.Currency,
                        Note = c.Note
                    }).ToList();
                var incomingSigs = new HashSet<string>(incoming.Select(CashSignature));
                var existing = await cashTxnRepo.GetByAccountAsync(acctId, ct);
                var toDelete = existing.Where(e => incomingSigs.Contains(CashSignature(e))).ToList();
                if (toDelete.Count > 0)
                    await cashTxnRepo.DeleteRangeAsync(toDelete, ct);
                await cashTxnRepo.AddRangeAsync(incoming, ct);
            }
            await cashTxnRepo.SaveChangesAsync(ct);
        }

        // 5) 按标的已实现/未实现盈亏（来自 SymbolSummary）：整批替换覆盖
        //    覆盖报表内全部账户，避免空仓后旧 SymbolSummary 残留把未实现浮盈撑大。
        {
            var accountIds = snap.Accounts.Select(a => a.AccountId)
                .Concat(snap.RealizedPnlsOrEmpty.Select(r => r.AccountId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rows = snap.RealizedPnlsOrEmpty.Select(r => new RealizedPnl
            {
                AccountId = r.AccountId,
                Symbol = r.Symbol,
                AssetCategory = r.AssetCategory,
                Description = r.Description,
                Currency = r.Currency,
                Realized = r.RealizedPnl,
                Unrealized = r.UnrealizedPnl,
                IngestedAtUtc = DateTime.UtcNow
            }).ToList();
            if (accountIds.Count > 0)
            {
                await realizedPnlRepo.ReplaceByAccountsAsync(accountIds, rows, ct);
                await realizedPnlRepo.SaveChangesAsync(ct);
            }
        }

        // 6) 逐日净值（来自 Flex "Net Asset Value (NAV) in Base" 的 EquitySummaryByReportDateInBase，IBKR 权威逐日值）
        //    TotalMarketValue = 真实持仓市值(PositionsMv)，CashBalance = Total - PositionsMv（现金+应计），
        //    保证 NetLiquidation(=TotalMarketValue+CashBalance) = Total 精确等于 IBKR 官方 NAV。
        if (snap.DailyNavsOrEmpty.Any())
        {
            // 逐账户写入
            foreach (var acctId in snap.DailyNavsOrEmpty.Select(n => n.AccountId).Distinct())
            {
                foreach (var nav in snap.DailyNavsOrEmpty.Where(n => n.AccountId == acctId)
                                                        .OrderBy(n => n.DateUtc))
                {
                    var day = DateTime.SpecifyKind(nav.DateUtc.Date, DateTimeKind.Utc);
                    // Flex 有时只回现金/保证金、PositionsMv=0（尤其当日未收盘），会把 NAV 写成巨额负值。
                    if (nav.PositionsMv == 0m && nav.Total < 0m)
                    {
                        var prev = await snapshotRepo.GetByDateAsync(acctId, day.AddDays(-1), ct)
                                   ?? await snapshotRepo.GetByDateAsync(acctId, day.AddDays(-2), ct);
                        if (prev is not null && prev.NetLiquidation > 10_000m)
                            continue;
                    }
                    if (nav.PositionsMv == 0m && day == DateTime.UtcNow.Date)
                        continue;

                    await snapshotRepo.UpsertAsync(new DailySnapshot
                    {
                        AccountId = acctId,
                        DateUtc = day,
                        TotalMarketValue = nav.PositionsMv,
                        CashBalance = nav.Total - nav.PositionsMv,
                        TotalUnrealizedPnl = 0m,
                        TotalRealizedPnl = 0m,
                        TotalPnl = 0m,
                        PositionCount = 0,
                        Source = DataSources.Flex
                    }, ct);
                }
            }
            // 不再写入 AccountId=ALL：Paper/Live 分次摄入会互相覆盖，造成「持仓 Paper + 净值 Live」串盘。
            // 跨账户汇总在 DailyPnlService 读时按账户列表聚合。
            await snapshotRepo.SaveChangesAsync(ct);
        }

        // 7) 逐标的逐日 MTM（来自 Flex "Mark-to-Market Performance Summary in Base"，Breakout by Day）
        //    先按 (账户,日,标的) 去重合并，再写逐账户；ALL 按 (日,标的) 跨账户汇总数量与当日盈亏。
        if (snap.MtmDailyOrEmpty.Any())
        {
            var perAccount = snap.MtmDailyOrEmpty
                .GroupBy(m => (
                    AccountId: m.AccountId,
                    Day: m.DateUtc.Date,
                    Symbol: m.Symbol.Trim().ToUpperInvariant()))
                .Select(g =>
                {
                    var last = g.Last();
                    var mp = last.ClosePrice * (last.Multiplier == 0 ? 1m : last.Multiplier);
                    return new
                    {
                        g.Key.AccountId,
                        Date = DateTime.SpecifyKind(g.Key.Day, DateTimeKind.Utc),
                        Symbol = last.Symbol.Trim(),
                        last.AssetType,
                        Quantity = g.Sum(x => x.CloseQuantity),
                        MarketPrice = mp,
                        DailyPnl = g.Sum(x => x.Total)
                    };
                });

            foreach (var row in perAccount)
            {
                await positionSnapshotRepo.UpsertAsync(new PositionDailySnapshot
                {
                    AccountId = row.AccountId,
                    DateUtc = row.Date,
                    Symbol = row.Symbol,
                    AssetType = row.AssetType,
                    Currency = "USD",
                    Quantity = row.Quantity,
                    AvgCost = 0m,
                    MarketPrice = row.MarketPrice,
                    DailyPnl = row.DailyPnl
                }, ct);
            }

            // 不写 PositionDailySnapshot AccountId=ALL（同日净值 ALL，会被分环境摄入覆盖）。
            await positionSnapshotRepo.SaveChangesAsync(ct);
        }

        // 摄入后：若 Flex 逐日净值已覆盖到最近交易日，则不再 CaptureToday（避免用持仓×市价估算覆盖 IBKR 官方 NAV）；
        // 仅当 Flex 未覆盖“今天”（报表尚未含今日，如盘中摄入）时才补一条实时快照。
        var latestFlexDate = snap.DailyNavsOrEmpty.Any() ? snap.DailyNavsOrEmpty.Max(n => n.DateUtc.Date) : (DateTime?)null;
        var todayTrading = TradingCalendar.LatestTradingDay(DateTime.UtcNow);
        // Flex 未带持仓时 CaptureToday 会用空持仓 + 旧现金估算 NAV，反而制造 -99% 假暴跌；跳过。
        if ((latestFlexDate is null || latestFlexDate < todayTrading) && flexHasAnyPositions)
        {
            foreach (var a in snap.Accounts)
                await snapshotService.CaptureTodayAsync(a.AccountId, null, null, ct);
            await snapshotService.CaptureTodayAsync(null, null, null, ct);
        }

        // 历史成交/净值只靠 Flex。Gateway 未开时不要连端口，否则会拖死这次摄入。
        await feedStatusRepo.UpsertAsync(new DataFeedStatus
        {
            Feed = DataSources.Flex,
            Environment = env.ToString(),
            LastSuccessUtc = DateTime.UtcNow,
            AccountId = snap.Accounts.FirstOrDefault()?.AccountId,
            Message =
                $"accounts={snap.Accounts.Count}, positions={snap.Positions.Count}, trades={snap.Trades.Count}, " +
                $"navDays={snap.DailyNavsOrEmpty.Count()}, queryId={qid}"
        }, ct);
        await feedStatusRepo.SaveChangesAsync(ct);
        logger?.LogInformation(
            "Flex（{Env}）已入库 accounts={Accounts} trades={Trades} navDays={NavDays}，未连 Gateway",
            env, snap.Accounts.Count, snap.Trades.Count, snap.DailyNavsOrEmpty.Count());
    }

    /// <summary>依次摄入所有已配置默认 queryId 的环境（Live / Paper）。未配置默认 queryId 的环境自动跳过。</summary>
    public async Task IngestAllAsync(CancellationToken ct = default)
    {
        foreach (var env in new[] { TradeEnv.Live, TradeEnv.Paper })
        {
            var (_, _, defaultQid) = ResolveEnv(env);
            if (string.IsNullOrWhiteSpace(defaultQid)) continue;
            await IngestAsync(env, null, ct);
        }
    }

    /// <summary>
    /// 按环境解析配置：token 必填；baseUrl 与默认 queryId 可选。
    /// 来源优先级：环境变量 &gt; appsettings.json（IBKR:Flex:Live / IBKR:Flex:Paper）。
    /// Live 兼容旧变量名 IBKR_FLEX_TOKEN（无后缀）。
    /// </summary>
    private (string Token, string? BaseUrl, string? DefaultQueryId) ResolveEnv(TradeEnv env)
    {
        var suffix = env == TradeEnv.Paper ? "PAPER" : "LIVE";
        var section = env == TradeEnv.Paper ? "Paper" : "Live";

        var token = Environment.GetEnvironmentVariable($"IBKR_FLEX_TOKEN_{suffix}")
                    ?? (env == TradeEnv.Live ? Environment.GetEnvironmentVariable("IBKR_FLEX_TOKEN") : null)
                    ?? NullIfEmpty(configuration[$"IBKR:Flex:{section}:Token"])
                    ?? throw new InvalidOperationException(
                        $"缺少 Flex Token：请设置环境变量 IBKR_FLEX_TOKEN_{suffix}" +
                        (env == TradeEnv.Live ? "（或 IBKR_FLEX_TOKEN）" : "") +
                        $"，或在 appsettings.Secrets.json / appsettings.json 配置 IBKR:Flex:{section}:Token。");

        var baseUrl = Environment.GetEnvironmentVariable($"IBKR_FLEX_BASE_URL_{suffix}")
                      ?? Environment.GetEnvironmentVariable("IBKR_FLEX_BASE_URL")
                      ?? NullIfEmpty(configuration[$"IBKR:Flex:{section}:BaseUrl"])
                      ?? NullIfEmpty(configuration["IBKR:Flex:BaseUrl"]);

        var defaultQueryId = Environment.GetEnvironmentVariable($"IBKR_FLEX_QUERYID_{suffix}")
                             ?? NullIfEmpty(configuration[$"IBKR:Flex:{section}:QueryId"]);

        return (token, baseUrl, defaultQueryId);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>成交去重签名：账户+标的+时间+方向+价格+数量+佣金，足以区分同一报表的重复行。
    /// 时间用 Kind 无关的固定格式（到秒）；金额用归一化格式（去尾零），避免 DB 读出的 numeric(18,6)
    /// （如 78.250000）与刚解析的 decimal（如 78.25）ToString 不一致而误判为不同签名、重复入库。</summary>
    private static string TradeSignature(Trade t) =>
        $"{t.AccountId}|{t.Symbol}|{FmtTradeTime(t.TradeTimeUtc)}|{t.Side}|{FmtDec(t.Price)}|{FmtDec(t.Quantity)}|{FmtDec(t.Commission)}";

    private static string TradeSignature(FlexTradeDto t) =>
        $"{t.AccountId}|{t.Symbol}|{FmtTradeTime(t.TradeTimeUtc)}|{t.Side}|{FmtDec(t.Price)}|{FmtDec(t.Quantity)}|{FmtDec(t.Commission)}";

    private static string FmtTradeTime(DateTime t) =>
        t.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private static string FmtDec(decimal d) =>
        d.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>现金流去重签名：账户+日期+类型+金额+币种+备注，用于与报表合并而保留手动录入。</summary>
    private static string CashSignature(CashTransaction t) =>
        $"{t.AccountId}|{t.DateUtc.Date:yyyy-MM-dd}|{t.Type}|{t.Amount}|{t.Currency}|{t.Note ?? ""}";
}
