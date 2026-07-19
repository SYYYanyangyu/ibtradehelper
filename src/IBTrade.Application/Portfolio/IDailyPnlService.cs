using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Portfolio;

/// <summary>每日盈亏查询用例。</summary>
public interface IDailyPnlService
{
    /// <summary>
    /// 返回最近 days 天的每日盈亏序列（按日期升序）。
    /// accountId 有值：该账户；否则若 env 有值：汇总该环境下全部账户；否则用历史 ALL 键。
    /// </summary>
    Task<IReadOnlyList<DailyPnlPointDto>> GetAsync(
        string? accountId,
        int days = 30,
        TradeEnv? env = null,
        CancellationToken ct = default);
}
