using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.Backtest.Strategies;

/// <summary>
/// 唐奇安通道突破（海龟交易法入门，趋势跟踪）：收盘价突破前 N 日最高 → 买入（追涨）；
/// 跌破前 N 日最低 → 卖出（止损/反手）。对单边趋势友好；震荡行情会反复止损。
/// 参数：period（通道周期，默认 20）。
/// </summary>
public class DonchianBreakoutStrategy : IStrategy
{
    public string Key => "donchian";
    public string Name => "唐奇安通道突破 (趋势跟踪)";

    public IReadOnlyList<StrategyParam> Parameters => new[]
    {
        new StrategyParam("period", "通道周期", 20, 1, 2, 200),
    };

    public IReadOnlyList<SignalType> GenerateSignals(
        IReadOnlyList<CandleDto> candles,
        IReadOnlyDictionary<string, double>? parameters)
    {
        int period = StrategyParam.GetInt(parameters, "period", 20);
        if (period < 1) period = 20;

        var highs = candles.Select(c => c.High).ToList();
        var lows = candles.Select(c => c.Low).ToList();

        var signals = new SignalType[candles.Count];
        for (int i = period; i < candles.Count; i++)
        {
            decimal highest = decimal.MinValue, lowest = decimal.MaxValue;
            for (int j = i - period; j < i; j++)
            {
                if (highs[j] > highest) highest = highs[j];
                if (lows[j] < lowest) lowest = lows[j];
            }
            if (candles[i].Close > highest) signals[i] = SignalType.Buy;
            else if (candles[i].Close < lowest) signals[i] = SignalType.Sell;
        }
        return signals;
    }
}
