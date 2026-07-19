using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

public interface IDataFeedStatusRepository
{
    Task<IReadOnlyList<DataFeedStatus>> GetAllAsync(CancellationToken ct = default);

    /// <summary>按 Feed + Environment 幂等更新最近成功时间。</summary>
    Task UpsertAsync(DataFeedStatus status, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
