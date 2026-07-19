namespace IBTrade.Application.Portfolio;

/// <summary>组合净值快照用例：把"今天"的组合状态固化成一条每日快照。</summary>
public interface ISnapshotService
{
    /// <summary>
    /// 抓取"今天"的组合净值并写入每日快照。
    /// accountId 为空表示汇总所有账户（记为 "ALL"）。
    /// cashBalance 为可选现金余额（当前持仓数据不含现金，可留空）。
    /// asOf 为可选日期（UTC），用于回填历史某天的快照；留空则取今天（自动对齐到最近交易日）。
    /// 返回实际落库的快照日期（已对齐到交易日）。
    /// </summary>
    Task<DateTime> CaptureTodayAsync(string? accountId, decimal? cashBalance = null, DateTime? asOf = null, CancellationToken ct = default);

}
