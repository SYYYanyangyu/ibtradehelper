namespace IBTrade.Application.Quant.TrendScalper;

/// <summary>Candidate rules use only bars supplied by the caller (completed bars in the engine).</summary>
public static class TrendResearch
{
    // Simple mean of the last N true ranges, including overnight gaps only if supplied.
    public static decimal? Atr(IReadOnlyList<EtBar> bars, int period)
    {
        if (period < 2 || bars.Count < period + 1) return null;
        decimal sum = 0;
        for (var i = bars.Count - period; i < bars.Count; i++)
            sum += Math.Max(bars[i].High - bars[i].Low,
                Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close)));
        return sum > 0 ? sum / period : null;
    }

    public static bool Retest(IReadOnlyList<EtBar> bars, string right, decimal level, int lookback, decimal tolerancePct)
    {
        if (bars.Count < 2 || level <= 0) return false;
        var last = bars[^1];
        var prior = bars.Skip(Math.Max(0, bars.Count - 1 - lookback)).Take(Math.Min(lookback, bars.Count - 1))
            .Where(b => b.StartEt.TimeOfDay >= new TimeSpan(10, 0, 0)).ToList();
        var tolerance = level * tolerancePct / 100m;
        return right == "C"
            ? prior.Any(b => b.Close > level) && last.Low >= level - tolerance && last.Low <= level + tolerance
                && last.Close > level && last.Close > last.Open
            : prior.Any(b => b.Close < level) && last.High <= level + tolerance && last.High >= level - tolerance
                && last.Close < level && last.Close < last.Open;
    }
}
