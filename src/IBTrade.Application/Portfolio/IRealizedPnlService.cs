using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Portfolio;

/// <summary>按标的历史盈亏用例服务接口。</summary>
public interface IRealizedPnlService
{
    /// <summary>
    /// 返回按标的的已实现/未实现盈亏看板。
    /// 不传 accountId 汇总所有账户；传值则只看该账户。
    /// </summary>
    Task<RealizedPnlSummaryDto> GetAsync(string? accountId, TradeEnv? env = null, CancellationToken ct = default);
}
