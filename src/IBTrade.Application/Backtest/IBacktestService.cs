using IBTrade.Application.Backtest.Dtos;
using IBTrade.Application.Backtest.Strategies;

namespace IBTrade.Application.Backtest;

/// <summary>回测服务：拉历史数据 → 跑策略 → 撮合 → 输出绩效。完全独立于看板/组合。</summary>
public interface IBacktestService
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default);

    /// <summary>列出可用策略（含参数元数据）。</summary>
    IReadOnlyList<StrategyInfo> ListStrategies();
}
