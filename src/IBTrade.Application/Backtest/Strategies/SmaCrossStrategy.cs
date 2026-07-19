using IBTrade.Application.Backtest.Indicators;
using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.Backtest.Strategies;

/// <summary>
/// 均线交叉策略（示例）：快线上穿慢线 → 买入；快线下穿慢线 → 卖出。
/// 参数：fast（快线周期，默认 10）、slow（慢线周期，默认 30）。
/// </summary>
public class SmaCrossStrategy : IStrategy
{
    public string Key => "sma_cross";
    public string Name => "均线交叉 (SMA Cross)";

    public IReadOnlyList<StrategyParam> Parameters => new[]
    {
        new StrategyParam("fast", "快线周期", 10, 1, 2, 400),
        new StrategyParam("slow", "慢线周期", 30, 1, 3, 500),
    };

    public IReadOnlyList<SignalType> GenerateSignals(
        IReadOnlyList<CandleDto> candles,
        IReadOnlyDictionary<string, double>? parameters)
    {
        int fast = GetInt(parameters, "fast", 10);
        int slow = GetInt(parameters, "slow", 30);
        if (fast >= slow) (fast, slow) = (Math.Min(fast, slow), Math.Max(fast, slow) + 1);

        var closes = candles.Select(c => c.Close).ToList();
        var fastMa = MovingAverage.Sma(closes, fast);
        var slowMa = MovingAverage.Sma(closes, slow);

        var signals = new SignalType[candles.Count];
        for (int i = 1; i < candles.Count; i++)
        {
            if (fastMa[i] is null || slowMa[i] is null || fastMa[i - 1] is null || slowMa[i - 1] is null)
                continue;

            bool prevBelow = fastMa[i - 1] <= slowMa[i - 1];
            bool nowAbove = fastMa[i] > slowMa[i];
            bool prevAbove = fastMa[i - 1] >= slowMa[i - 1];
            bool nowBelow = fastMa[i] < slowMa[i];

            if (prevBelow && nowAbove) signals[i] = SignalType.Buy;
            else if (prevAbove && nowBelow) signals[i] = SignalType.Sell;
        }
        return signals;
    }

    private static int GetInt(IReadOnlyDictionary<string, double>? p, string key, int fallback)
        => p is not null && p.TryGetValue(key, out var v) && v > 0 ? (int)v : fallback;
}
