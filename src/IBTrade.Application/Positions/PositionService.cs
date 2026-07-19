using IBTrade.Application.Positions.Dtos;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Positions;

/// <summary>
/// 持仓用例服务实现：编排领域对象与仓储，不关心具体数据库/HTTP 细节。
/// </summary>
public class PositionService(IPositionRepository repository) : IPositionService
{
    public async Task<IReadOnlyList<PositionDto>> GetPositionsAsync(string? accountId, CancellationToken ct = default)
    {
        var positions = string.IsNullOrWhiteSpace(accountId)
            ? await repository.GetAllAsync(ct)
            : await repository.GetByAccountAsync(accountId, ct);

        return positions.Select(PositionDto.FromEntity).ToList();
    }
}
