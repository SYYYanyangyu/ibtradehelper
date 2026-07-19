using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

/// <summary>外部现金流仓储接口。</summary>
public interface ICashTransactionRepository
{
    /// <summary>取某账户自 fromUtc（含）起的全部现金流，按日期升序。</summary>
    Task<IReadOnlyList<CashTransaction>> GetSinceAsync(string accountId, DateTime fromUtc, CancellationToken ct = default);

    /// <summary>取某账户的全部现金流（用于合并去重，避免整账户替换冲掉手动录入）。</summary>
    Task<IReadOnlyList<CashTransaction>> GetByAccountAsync(string accountId, CancellationToken ct = default);

    /// <summary>追加一条现金流记录。</summary>
    Task AddAsync(CashTransaction txn, CancellationToken ct = default);

    /// <summary>批量追加现金流记录。</summary>
    Task AddRangeAsync(IEnumerable<CashTransaction> txns, CancellationToken ct = default);

    /// <summary>批量删除现金流记录（按实体主键）。</summary>
    Task DeleteRangeAsync(IEnumerable<CashTransaction> txns, CancellationToken ct = default);

    /// <summary>
    /// 幂等替换某账户的全部现金流（先删后插）：用于 Flex 报表整批入库，避免重复累积。
    /// </summary>
    Task ReplaceByAccountAsync(string accountId, IEnumerable<CashTransaction> txns, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
