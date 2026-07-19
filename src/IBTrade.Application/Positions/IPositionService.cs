using IBTrade.Application.Positions.Dtos;

namespace IBTrade.Application.Positions;

/// <summary>持仓用例服务接口。</summary>
public interface IPositionService
{
    Task<IReadOnlyList<PositionDto>> GetPositionsAsync(string? accountId, CancellationToken ct = default);
}
