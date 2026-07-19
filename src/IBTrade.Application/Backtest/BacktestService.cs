using IBTrade.Application.Backtest.Dtos;
using IBTrade.Application.Backtest.Strategies;
using IBTrade.Application.MarketData;
using IBTrade.Application.MarketData.Dtos;

namespace IBTrade.Application.Backtest;

/// <summary>
/// 回测引擎：单标的、多头（long-only）、全仓进出。
/// 默认撮合：信号出现在第 i 根 → 第 i+1 根开盘价成交（+滑点/佣金），消除同 bar 收盘成交的前瞻偏差。
/// 成本：固定佣金（进出各一笔）+ 单边滑点（买上浮/卖下浮），结果回显累计成本与拖累。
/// </summary>
public class BacktestService(IHistoricalDataService historicalData, IStrategyFactory strategyFactory)
    : IBacktestService
{
    public IReadOnlyList<StrategyInfo> ListStrategies() => strategyFactory.List();

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            throw new ArgumentException("Symbol 必填");
        if (request.InitialCapital <= 0)
            throw new ArgumentException("初始资金必须为正");

        var candles = await historicalData.GetBarsAsync(
            request.Symbol, request.Exchange, request.Currency,
            request.Duration, request.BarSize, ct);

        if (candles.Count < 3)
            throw new InvalidOperationException("历史数据不足（少于 3 根 K 线），无法用次日开盘模型回测。请检查标的/区间/行情订阅。");

        var (strategy, parameters) = Ai.StrategyParameterSanitizer.SanitizeStrategy(
            strategyFactory, request.Strategy, request.Parameters);
        request.Strategy = strategy.Key;
        request.Parameters = parameters;
        request.SlippageBps = Math.Clamp(request.SlippageBps, 0m, 500m);
        request.CommissionPerTrade = Math.Clamp(request.CommissionPerTrade, 0m, 1_000m);
        if (!string.Equals(request.FillMode, "close", StringComparison.OrdinalIgnoreCase))
            request.FillMode = "next_open";

        var signals = strategy.GenerateSignals(candles, request.Parameters);
        return Simulate(request, candles, signals, strategy.Name);
    }

    private static BacktestResult Simulate(
        BacktestRequest req, IReadOnlyList<CandleDto> candles,
        IReadOnlyList<SignalType> signals, string strategyName)
    {
        decimal cash = req.InitialCapital;
        decimal position = 0m;
        decimal entryPrice = 0m;
        decimal entrySlippageCost = 0m;
        DateTime entryTime = default;

        var trades = new List<TradeRecord>();
        var equityCurve = new List<EquityPoint>(candles.Count);
        var barReturns = new List<double>(candles.Count);

        decimal prevEquity = req.InitialCapital;
        decimal peakEquity = req.InitialCapital;
        decimal maxDrawdown = 0m;
        decimal totalCommission = 0m;
        decimal totalSlippage = 0m;

        var useNextOpen = !string.Equals(req.FillMode, "close", StringComparison.OrdinalIgnoreCase);
        SignalType? pending = null;

        for (int i = 0; i < candles.Count; i++)
        {
            var bar = candles[i];

            if (useNextOpen && pending is not null)
            {
                var raw = bar.Open > 0 ? bar.Open : bar.Close;
                Fill(pending.Value, raw, bar.Time, req,
                    ref cash, ref position, ref entryPrice, ref entrySlippageCost, ref entryTime,
                    ref totalCommission, ref totalSlippage, trades);
                pending = null;
            }

            var signal = signals[i];

            if (useNextOpen)
            {
                if (i == candles.Count - 1)
                {
                    if (signal == SignalType.Sell && position > 0)
                    {
                        Fill(SignalType.Sell, bar.Close, bar.Time, req,
                            ref cash, ref position, ref entryPrice, ref entrySlippageCost, ref entryTime,
                            ref totalCommission, ref totalSlippage, trades);
                    }
                }
                else if (signal is SignalType.Buy or SignalType.Sell)
                {
                    pending = signal;
                }
            }
            else if (signal is SignalType.Buy or SignalType.Sell)
            {
                Fill(signal, bar.Close, bar.Time, req,
                    ref cash, ref position, ref entryPrice, ref entrySlippageCost, ref entryTime,
                    ref totalCommission, ref totalSlippage, trades);
            }

            decimal mark = bar.Close > 0 ? bar.Close : (bar.Open > 0 ? bar.Open : 0m);
            decimal equity = cash + position * mark;
            equityCurve.Add(new EquityPoint(bar.Time, equity));

            if (i > 0 && prevEquity > 0)
                barReturns.Add((double)((equity - prevEquity) / prevEquity));
            prevEquity = equity;

            if (equity > peakEquity) peakEquity = equity;
            if (peakEquity > 0)
            {
                decimal dd = (peakEquity - equity) / peakEquity * 100m;
                if (dd > maxDrawdown) maxDrawdown = dd;
            }
        }

        var last = candles[^1];
        if (position > 0)
        {
            Fill(SignalType.Sell, last.Close, last.Time, req,
                ref cash, ref position, ref entryPrice, ref entrySlippageCost, ref entryTime,
                ref totalCommission, ref totalSlippage, trades);
        }

        decimal finalEquity = cash;
        decimal totalReturn = req.InitialCapital > 0
            ? (finalEquity - req.InitialCapital) / req.InitialCapital * 100m : 0m;

        decimal first = candles[0].Close;
        decimal buyHold = first > 0 ? (last.Close - first) / first * 100m : 0m;

        int wins = trades.Count(t => t.ProfitLoss > 0);
        decimal winRate = trades.Count > 0 ? (decimal)wins / trades.Count * 100m : 0m;

        decimal totalCost = totalCommission + totalSlippage;
        decimal costDrag = req.InitialCapital > 0 ? totalCost / req.InitialCapital * 100m : 0m;
        decimal approxGross = req.InitialCapital > 0
            ? (finalEquity + totalCost - req.InitialCapital) / req.InitialCapital * 100m : 0m;

        return new BacktestResult
        {
            Symbol = req.Symbol,
            Strategy = strategyName,
            StartTime = candles[0].Time,
            EndTime = last.Time,
            BarCount = candles.Count,
            InitialCapital = req.InitialCapital,
            FinalEquity = Round(finalEquity),
            TotalReturnPct = Round(totalReturn),
            BuyHoldReturnPct = Round(buyHold),
            MaxDrawdownPct = Round(maxDrawdown),
            TradeCount = trades.Count,
            WinCount = wins,
            WinRatePct = Round(winRate),
            Sharpe = Round((decimal)Sharpe(barReturns, req.BarSize)),
            FillMode = req.FillMode,
            SlippageBps = req.SlippageBps,
            CommissionPerTrade = req.CommissionPerTrade,
            TotalCommission = Round(totalCommission),
            TotalSlippageCost = Round(totalSlippage),
            TotalCost = Round(totalCost),
            CostDragPct = Round(costDrag),
            ApproxGrossReturnPct = Round(approxGross),
            Trades = trades,
            EquityCurve = equityCurve
        };
    }

    /// <summary>按参考价成交：先算滑点成交价，再扣佣金，并累计成本。</summary>
    private static void Fill(
        SignalType signal, decimal rawPx, DateTime time, BacktestRequest req,
        ref decimal cash, ref decimal position,
        ref decimal entryPrice, ref decimal entrySlippageCost, ref DateTime entryTime,
        ref decimal totalCommission, ref decimal totalSlippage,
        List<TradeRecord> trades)
    {
        if (rawPx <= 0) return;
        var fillPx = ApplySlippage(rawPx, signal, req.SlippageBps);
        if (fillPx <= 0) return;

        if (signal == SignalType.Buy && position == 0)
        {
            decimal investable = cash - req.CommissionPerTrade;
            if (investable <= 0) return;
            decimal qty = Math.Floor(investable / fillPx);
            if (qty <= 0) return;

            var slipCost = (fillPx - rawPx) * qty;
            cash -= qty * fillPx + req.CommissionPerTrade;
            position = qty;
            entryPrice = fillPx;
            entrySlippageCost = slipCost;
            entryTime = time;
            totalCommission += req.CommissionPerTrade;
            totalSlippage += slipCost;
        }
        else if (signal == SignalType.Sell && position > 0)
        {
            var slipCost = (rawPx - fillPx) * position;
            cash += position * fillPx - req.CommissionPerTrade;
            totalCommission += req.CommissionPerTrade;
            totalSlippage += slipCost;

            var roundCommission = req.CommissionPerTrade * 2m;
            var roundSlip = entrySlippageCost + slipCost;
            // 净盈亏：价差（含滑点成交价）- 双边佣金
            decimal pnl = (fillPx - entryPrice) * position - roundCommission;
            decimal retPct = entryPrice > 0 ? (fillPx - entryPrice) / entryPrice * 100m : 0m;
            trades.Add(new TradeRecord(
                entryTime, entryPrice, time, fillPx, position,
                Round(pnl), Round(retPct),
                Round(roundCommission), Round(roundSlip)));

            position = 0;
            entryPrice = 0;
            entrySlippageCost = 0;
        }
    }

    private static decimal ApplySlippage(decimal price, SignalType side, decimal bps)
    {
        if (price <= 0 || bps <= 0) return price;
        var adj = bps / 10_000m;
        return side == SignalType.Buy ? price * (1m + adj) : price * (1m - adj);
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static double Sharpe(List<double> returns, string barSize)
    {
        if (returns.Count < 2) return 0;
        double mean = returns.Average();
        double variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        double std = Math.Sqrt(variance);
        if (std == 0) return 0;

        double periodsPerYear = barSize.Contains("day", StringComparison.OrdinalIgnoreCase) ? 252
            : barSize.Contains("hour", StringComparison.OrdinalIgnoreCase) ? 252 * 6.5
            : barSize.Contains("min", StringComparison.OrdinalIgnoreCase) ? 252 * 390
            : 252;

        return mean / std * Math.Sqrt(periodsPerYear);
    }
}
