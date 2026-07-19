using System.Text.RegularExpressions;
using IBTrade.Application.Backtest.Strategies;

namespace IBTrade.Application.Ai;

/// <summary>
/// 策略入参消毒：仅允许白名单策略 key，参数强制钳制到元数据 Min/Max。
/// AI / 回测部署共用，杜绝越界参数与任意代码执行面。
/// </summary>
public static class StrategyParameterSanitizer
{
    private static readonly Regex SymbolRe = new(@"^[A-Za-z0-9.\-]{1,16}$", RegexOptions.Compiled);

    public static string SanitizeSymbol(string? symbol)
    {
        var s = (symbol ?? "").Trim().ToUpperInvariant();
        if (!SymbolRe.IsMatch(s))
            throw new ArgumentException("标的代码非法：仅允许字母/数字/点/短横，最长 16 位。");
        return s;
    }

    public static string SanitizeDuration(string? duration)
    {
        var d = string.IsNullOrWhiteSpace(duration) ? "1 Y" : duration.Trim();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1 D", "60 D", "1 M", "3 M", "6 M", "1 Y", "2 Y", "3 Y", "5 Y", "YTD"
        };
        if (!allowed.Contains(d))
            throw new ArgumentException($"不支持的区间 '{d}'。");
        return d;
    }

    public static string SanitizeBarSize(string? barSize)
    {
        var b = string.IsNullOrWhiteSpace(barSize) ? "1 day" : barSize.Trim();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1 min", "5 mins", "15 mins", "30 mins", "1 hour", "1 day"
        };
        if (!allowed.Contains(b))
            throw new ArgumentException($"不支持的 K 线粒度 '{b}'。");
        return b;
    }

    /// <summary>校验策略存在，并把参数钳制到声明范围；缺省补默认值。</summary>
    public static (IStrategy Strategy, Dictionary<string, double> Parameters) SanitizeStrategy(
        IStrategyFactory factory,
        string? strategyKey,
        IReadOnlyDictionary<string, double>? raw)
    {
        var strategy = factory.Get(string.IsNullOrWhiteSpace(strategyKey) ? "sma_cross" : strategyKey!);
        var clean = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var meta in strategy.Parameters)
        {
            double v = meta.Default;
            if (raw is not null && raw.TryGetValue(meta.Key, out var incoming) && !double.IsNaN(incoming) && !double.IsInfinity(incoming))
                v = incoming;
            if (meta.Min is not null) v = Math.Max(meta.Min.Value, v);
            if (meta.Max is not null) v = Math.Min(meta.Max.Value, v);
            // 步长对齐（整数类参数）
            if (meta.Step >= 1) v = Math.Round(v / meta.Step) * meta.Step;
            clean[meta.Key] = v;
        }

        // 策略特有一致性：均线快线必须 < 慢线
        if (strategy.Key.Equals("sma_cross", StringComparison.OrdinalIgnoreCase)
            && clean.TryGetValue("fast", out var fast)
            && clean.TryGetValue("slow", out var slow)
            && fast >= slow)
        {
            clean["fast"] = Math.Min(fast, slow - 1);
            if (clean["fast"] < (strategy.Parameters.First(p => p.Key == "fast").Min ?? 2))
            {
                clean["fast"] = strategy.Parameters.First(p => p.Key == "fast").Min ?? 2;
                clean["slow"] = clean["fast"] + 1;
            }
        }

        return (strategy, clean);
    }

    public static decimal SanitizeQuantity(decimal quantity)
    {
        if (quantity <= 0) throw new ArgumentException("下单数量必须为正。");
        if (quantity > 100_000m) throw new ArgumentException("演练部署数量上限为 100,000（安全护栏）。");
        return Math.Round(quantity, 0);
    }
}
