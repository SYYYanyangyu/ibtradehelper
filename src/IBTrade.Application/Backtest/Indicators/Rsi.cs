namespace IBTrade.Application.Backtest.Indicators;

/// <summary>
/// 相对强弱指标 RSI（Wilder 平滑）。返回数组与输入等长，未形成有效值处为 null。
/// </summary>
public static class Rsi
{
    public static decimal?[] Compute(IReadOnlyList<decimal> closes, int period)
    {
        var result = new decimal?[closes.Count];
        if (period <= 1 || closes.Count <= period) return result;

        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff >= 0) avgGain += diff;
            else avgLoss -= diff;
        }
        avgGain /= period;
        avgLoss /= period;
        result[period] = From(avgGain, avgLoss);

        for (int i = period + 1; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            decimal gain = diff > 0 ? diff : 0;
            decimal loss = diff < 0 ? -diff : 0;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = From(avgGain, avgLoss);
        }
        return result;
    }

    private static decimal From(decimal avgGain, decimal avgLoss)
        => avgLoss == 0 ? 100m : 100m - 100m / (1m + avgGain / avgLoss);
}
