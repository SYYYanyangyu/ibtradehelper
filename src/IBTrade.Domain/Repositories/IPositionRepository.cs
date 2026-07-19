using IBTrade.Domain.Entities;

namespace IBTrade.Domain.Repositories;

/// <summary>
/// 持仓仓储接口：定义于领域层，实现于基础设施层（依赖倒置）。
/// </summary>
public interface IPositionRepository
{
    Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Position>> GetByAccountAsync(string accountId, CancellationToken ct = default);

    Task AddAsync(Position position, CancellationToken ct = default);

    /// <summary>幂等替换某账户的全部持仓（先删后插）：用于 Flex 报表整批覆盖。</summary>
    Task ReplaceByAccountAsync(string accountId, IEnumerable<Position> positions, CancellationToken ct = default);

    /// <summary>
    /// Gateway 合并：Flex 仓位只刷新市价；Gateway 自有仓位可改数量/均价；删除已平掉的 Gateway 仓位。
    /// 返回说明供状态条展示。
    /// </summary>
    Task<string> MergeGatewayMarksAsync(string accountId, IEnumerable<Position> gatewayPositions, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
