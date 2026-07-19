using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Portfolio;

/// <summary>个股每日盈亏归因服务：按日期差分出每只标的当日贡献。</summary>
public interface IPositionDailyPnlService
{
    /// <summary>
    /// 取指定日期（默认今天）各标的的当日贡献排行。
    /// 贡献 = 当日市值 − 前一日市值；已平仓标的贡献为 −前一日市值。
    /// env 用于「全部」按 Paper/Live 隔离，避免串盘。
    /// </summary>
    Task<PositionDailyPnlDto> GetByDateAsync(
        string? accountId,
        DateTime dateUtc,
        TradeEnv? env = null,
        CancellationToken ct = default);
}
