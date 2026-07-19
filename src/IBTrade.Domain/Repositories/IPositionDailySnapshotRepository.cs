using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

/// <summary>个股每日快照仓储接口（定义于领域层，实现于基础设施层）。</summary>
public interface IPositionDailySnapshotRepository
{
    /// <summary>取某账户某天的全部个股快照（可能多标的）。</summary>
    Task<IReadOnlyList<PositionDailySnapshot>> GetByDateAsync(string accountId, DateTime dateUtc, CancellationToken ct = default);

    /// <summary>取某账户在指定日期之前、最近一个有数据的快照日期（用于差分出当日贡献）。无则返回 null。</summary>
    Task<DateTime?> GetPreviousDateAsync(string accountId, DateTime dateUtc, CancellationToken ct = default);

    /// <summary>写入或更新某账户某天某标的的快照（同一账户同日同标的幂等）。</summary>
    Task UpsertAsync(PositionDailySnapshot snapshot, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
