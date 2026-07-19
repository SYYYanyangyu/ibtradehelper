namespace IBTrade.Application.IBKR;

/// <summary>TWS 实时同步用例：拉取实时账户快照并写入现有库。</summary>
public interface ITwsSyncService
{
    /// <summary>连接 TWS 拉取一次实时快照，并 upsert 持仓 / 账户 / 今日净值快照（含 ALL 聚合）。</summary>
    Task<TwsSnapshot> SyncAsync(CancellationToken ct = default);
}
