namespace IBTrade.Application.Backtest.Strategies;

/// <summary>
/// 策略参数元数据：用于前端按当前策略动态渲染参数输入框，无需为每种策略单独改前端。
/// </summary>
public record StrategyParam(
    string Key,
    string Label,
    double Default,
    double Step = 1,
    double? Min = null,
    double? Max = null)
{
    /// <summary>从请求参数里取整数（缺省/非法则用默认值）。</summary>
    public static int GetInt(IReadOnlyDictionary<string, double>? p, string key, int fallback)
        => p is not null && p.TryGetValue(key, out var v) && v > 0 ? (int)v : fallback;
}
