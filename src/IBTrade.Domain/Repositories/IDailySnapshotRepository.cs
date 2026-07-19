using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

/// <summary>每日净值快照仓储接口（定义于领域层，实现于基础设施层）。</summary>
public interface IDailySnapshotRepository
{
    /// <summary>取最近 days 天、指定账户的快照（按日期升序）。</summary>
    Task<IReadOnlyList<DailySnapshot>> GetRecentAsync(string accountId, int days, CancellationToken ct = default);

    /// <summary>取最近 days 天、多个账户快照，按日期聚合净值（用于 Live/Paper「全部」）。</summary>
    Task<IReadOnlyList<DailySnapshot>> GetRecentAggregatedAsync(IReadOnlyCollection<string> accountIds, int days, CancellationToken ct = default);

    /// <summary>按账户 + 日期取单条快照（日期只比较年月日）。</summary>
    Task<DailySnapshot?> GetByDateAsync(string accountId, DateTime dateUtc, CancellationToken ct = default);

    /// <summary>写入或更新某天的快照（同一账户同日幂等）。</summary>
    Task UpsertAsync(DailySnapshot snapshot, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
