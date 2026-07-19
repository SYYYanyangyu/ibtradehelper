using IBTrade.Domain.Enums;

namespace IBTrade.Application.Portfolio;

/// <summary>组合盈亏用例服务接口。</summary>
public interface IPortfolioService
{
    /// <summary>
    /// 计算组合看板：总市值、未实现盈亏、已实现盈亏，以及逐标的明细。
    /// 不传入 accountId 时汇总所有账户。
    /// </summary>
    Task<PortfolioSummaryDto> GetSummaryAsync(string? accountId, TradeEnv? env = null, CancellationToken ct = default);
}
