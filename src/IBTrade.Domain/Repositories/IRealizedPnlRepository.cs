using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

/// <summary>按标的已实现盈亏仓储接口。</summary>
public interface IRealizedPnlRepository
{
    Task<IReadOnlyList<RealizedPnl>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RealizedPnl>> GetByAccountAsync(string accountId, CancellationToken ct = default);

    /// <summary>
    /// 幂等替换指定账户集合的已实现盈亏（先删后插）：Flex 报表为全周期快照，整批覆盖即可。
    /// </summary>
    Task ReplaceByAccountsAsync(IEnumerable<string> accountIds, IEnumerable<RealizedPnl> rows, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
