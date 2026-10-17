using IBTrade.Application.Portfolio;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace IBTrade.Application.IBKR;

/// <summary>
/// IB Gateway 实时同步：刷新市价；不覆盖 Flex 报表的数量/均价与官方日净值。
/// 端口优先 Gateway（4002/4001）；TWS 桌面端口仅作兼容回退。
/// </summary>
public class TwsSyncService(
    TwsOptions options,
    ITwsClientFactory clientFactory,
    IAccountRepository accountRepo,
    IPositionRepository positionRepo,
    IDailySnapshotRepository snapshotRepo,
    IDataFeedStatusRepository feedStatusRepo,
    ILogger<TwsSyncService>? logger = null) : ITwsSyncService
{
    public async Task<TwsSnapshot> SyncAsync(CancellationToken ct = default)
    {
        Exception? lastError = null;
        var syncClientId = options.ClientId + 1;
        foreach (var port in options.PortsFor(options.Mode))
        {
            try
            {
                logger?.LogInformation("尝试连接 Gateway/API 端口 {Port}（clientId={ClientId}）…", port, syncClientId);
                using var client = clientFactory.Create(options.Host, port, syncClientId);
                var snap = await client.PullSnapshotAsync(ct);
                snap.ConnectedPort = port;
                // 统一展示为 Gateway（你们实际接入）；端口非 400x 时仍标 TWS 便于排查误连桌面端
                if (port is 4001 or 4002)
                    snap.Source = "Gateway";
                logger?.LogInformation("已通过端口 {Port}（{Source}）拉取账户 {Account}", port, snap.Source, snap.AccountId);
                return await PersistAsync(snap, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger?.LogWarning(ex, "端口 {Port} 连接失败，尝试下一个", port);
            }
        }

        throw new InvalidOperationException(
            $"无法连接到 {options.Mode} 端口（{string.Join(", ", options.PortsFor(options.Mode))}）。Paper 只连 4002/7497，Live 只连 4001/7496。",
            lastError);
    }

    private async Task<TwsSnapshot> PersistAsync(TwsSnapshot snap, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(snap.AccountId))
            throw new InvalidOperationException("Gateway 未返回账户号，可能尚未登录或 API 权限不足。");

        var existing = await accountRepo.GetByAccountIdAsync(snap.AccountId, ct);
        if (existing is null)
        {
            await accountRepo.AddAsync(new Account
            {
                AccountId = snap.AccountId,
                BaseCurrency = snap.Currency,
                Alias = "Gateway 实时接入",
                Environment = options.Mode == TradingMode.Live ? TradeEnv.Live : TradeEnv.Paper
            }, ct);
            await accountRepo.SaveChangesAsync(ct);
        }

        var now = DateTime.UtcNow;
        var positions = snap.Positions.Select(p => new Position
        {
            AccountId = snap.AccountId,
            Symbol = p.Symbol,
            AssetType = p.AssetType,
            Currency = p.Currency,
            Quantity = p.Quantity,
            AvgCost = p.AvgCost,
            MarketPrice = p.MarketPrice,
            Multiplier = p.Multiplier > 0 ? p.Multiplier : (p.AssetType == AssetType.Option ? 100m : 1m),
            Source = DataSources.Gateway,
            PriceSource = DataSources.Gateway
        }).ToList();

        var mergeMsg = await positionRepo.MergeGatewayMarksAsync(snap.AccountId, positions, ct);
        await positionRepo.SaveChangesAsync(ct);
        logger?.LogInformation("Gateway 持仓合并：{Msg}", mergeMsg);

        var navWritten = false;
        if (TradingCalendar.CanMutateDailySnapshot(now))
        {
            var today = now.Date;
            var totalMv = snap.Positions.Sum(p => p.MarketValue);
            var totalUn = snap.Positions.Sum(p => p.UnrealizedPnl);
            var totalRe = snap.Positions.Sum(p => p.RealizedPnl);

            // Source=Gateway：若当日已有 Flex 官方 NAV，仓储会拒绝覆盖
            await snapshotRepo.UpsertAsync(new DailySnapshot
            {
                AccountId = snap.AccountId,
                DateUtc = today,
                TotalMarketValue = totalMv,
                CashBalance = snap.CashBalance,
                TotalUnrealizedPnl = totalUn,
                TotalRealizedPnl = totalRe,
                TotalPnl = totalUn + totalRe,
                PositionCount = positions.Count,
                Source = DataSources.Gateway
            }, ct);

            // 不写 ALL 汇总行：与 Flex 分环境摄入冲突，读侧按账户列表聚合即可。
            await snapshotRepo.SaveChangesAsync(ct);
            navWritten = true;
        }
        else
        {
            logger?.LogInformation(
                "非交易日，跳过 DailySnapshot（已合并市价）；最近交易日={Day:yyyy-MM-dd}",
                TradingCalendar.LatestTradingDay(now));
        }

        await feedStatusRepo.UpsertAsync(new DataFeedStatus
        {
            Feed = DataSources.Gateway,
            Environment = "", // 空串占位，保证 (Feed, Environment) 唯一索引可幂等
            LastSuccessUtc = now,
            AccountId = snap.AccountId,
            ConnectedPort = snap.ConnectedPort,
            Message = $"{mergeMsg}; navWritten={navWritten}"
        }, ct);
        await feedStatusRepo.SaveChangesAsync(ct);

        return snap;
    }
}
