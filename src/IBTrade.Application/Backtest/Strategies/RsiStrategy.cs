using IBTrade.Application.Backtest.Indicators;
using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.Backtest.Strategies;

/// <summary>
/// RSI 超买超卖策略（均值回归）：RSI 跌破超卖线 → 买入（博反弹）；上穿超买线 → 卖出（止盈）。
/// 适合震荡行情；单边行情中 RSI 易钝化（长期超买不回头），可能回撤较大。
/// 参数：period（默认14）、oversold（默认30）、overbought（默认70）。
/// </summary>
public class RsiStrategy : IStrategy
{
    public string Key => "rsi";
    public string Name => "RSI 超买超卖 (均值回归)";

    public IReadOnlyList<StrategyParam> Parameters => new[]
    {
        new StrategyParam("period", "RSI 周期", 14, 1, 2, 100),
        new StrategyParam("oversold", "超卖线", 30, 1, 1, 99),
        new StrategyParam("overbought", "超买线", 70, 1, 2, 100),
    };

    public IReadOnlyList<SignalType> GenerateSignals(
        IReadOnlyList<CandleDto> candles,
        IReadOnlyDictionary<string, double>? parameters)
    {
        int period = StrategyParam.GetInt(parameters, "period", 14);
        int oversold = StrategyParam.GetInt(parameters, "oversold", 30);
        int overbought = StrategyParam.GetInt(parameters, "overbought", 70);
        if (oversold >= overbought) (oversold, overbought) = (30, 70);

        var rsi = Rsi.Compute(candles.Select(c => c.Close).ToList(), period);

        var signals = new SignalType[candles.Count];
        for (int i = period; i < candles.Count; i++)
        {
            if (rsi[i] is null) continue;
            if (rsi[i]! < oversold) signals[i] = SignalType.Buy;
            else if (rsi[i]! > overbought) signals[i] = SignalType.Sell;
        }
        return signals;
    }
}
