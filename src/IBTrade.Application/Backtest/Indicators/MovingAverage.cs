namespace IBTrade.Application.Backtest.Indicators;

/// <summary>常用移动平均指标。返回数组与输入等长，未形成有效值处为 null。</summary>
public static class MovingAverage
{
    /// <summary>简单移动平均 SMA。</summary>
    public static decimal?[] Sma(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal?[values.Count];
        if (period <= 0) return result;

        decimal sum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            if (i >= period - 1) result[i] = sum / period;
        }
        return result;
    }

    /// <summary>指数移动平均 EMA。</summary>
    public static decimal?[] Ema(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal?[values.Count];
        if (period <= 0 || values.Count == 0) return result;

        decimal k = 2m / (period + 1);
        decimal? prev = null;
        for (int i = 0; i < values.Count; i++)
        {
            if (i < period - 1) continue;
            if (prev is null)
            {
                // 用前 period 个值的 SMA 作为 EMA 种子
                decimal seed = 0;
                for (int j = i - period + 1; j <= i; j++) seed += values[j];
                prev = seed / period;
            }
            else
            {
                prev = values[i] * k + prev.Value * (1 - k);
            }
            result[i] = prev;
        }
        return result;
    }
}
