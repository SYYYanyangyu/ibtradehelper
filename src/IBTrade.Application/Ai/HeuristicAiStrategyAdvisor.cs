using IBTrade.Application.Ai.Dtos;
using IBTrade.Application.Backtest.Strategies;

namespace IBTrade.Application.Ai;

/// <summary>
/// 本地启发式顾问：无外网/无 API Key 时仍可用。
/// 基于关键词选择白名单策略并给出保守默认参数。
/// </summary>
public class HeuristicAiStrategyAdvisor(IStrategyFactory factory) : IAiStrategyAdvisor
{
    public Task<AiSuggestResult> SuggestAsync(AiSuggestRequest request, CancellationToken ct = default)
    {
        var prompt = (request.Prompt ?? "").Trim();
        if (prompt.Length is < 2 or > 2000)
            throw new ArgumentException("请用 2～2000 字描述交易想法。");

        var lower = prompt.ToLowerInvariant();
        // prompt 内标的优先于表单传入的默认 AAPL
        var symbol = PromptInstrumentResolver.ResolveSymbol(prompt, llmSymbol: null, request.Symbol);

        var (key, rationale, confidence, paramHints) = InferStrategy(lower);
        var (strategy, parameters) = StrategyParameterSanitizer.SanitizeStrategy(factory, key, paramHints);

        // 风格微调
        ApplyStyleHints(lower, strategy.Key, parameters);

        var duration = string.IsNullOrWhiteSpace(request.Duration)
            ? InferDuration(lower)
            : StrategyParameterSanitizer.SanitizeDuration(request.Duration);
        var barSize = string.IsNullOrWhiteSpace(request.BarSize)
            ? InferBarSize(lower)
            : StrategyParameterSanitizer.SanitizeBarSize(request.BarSize);

        // 再消毒一次（Infer 可能绕过白名单）
        duration = StrategyParameterSanitizer.SanitizeDuration(duration);
        barSize = StrategyParameterSanitizer.SanitizeBarSize(barSize);
        (_, parameters) = StrategyParameterSanitizer.SanitizeStrategy(factory, strategy.Key, parameters);

        return Task.FromResult(new AiSuggestResult
        {
            Symbol = symbol,
            StrategyKey = strategy.Key,
            StrategyName = strategy.Name,
            Parameters = parameters,
            Duration = duration,
            BarSize = barSize,
            Source = "heuristic",
            Rationale = rationale,
            Confidence = confidence
        });
    }

    private static (string Key, string Rationale, double Confidence, Dictionary<string, double> Hints) InferStrategy(string lower)
    {
        if (ContainsAny(lower, "rsi", "相对强弱", "超买", "超卖"))
            return ("rsi", "检测到 RSI/超买超卖语义，映射到白名单策略 rsi。", 0.72,
                new Dictionary<string, double> { ["period"] = 14, ["oversold"] = 30, ["overbought"] = 70 });

        if (ContainsAny(lower, "donchian", "唐奇安", "通道突破", "突破", "新高", "新低"))
            return ("donchian", "检测到通道/突破语义，映射到白名单策略 donchian。", 0.7,
                new Dictionary<string, double> { ["period"] = 20 });

        if (ContainsAny(lower, "sma", "均线", "金叉", "死叉", "交叉", "ma ", "moving average"))
            return ("sma_cross", "检测到均线交叉语义，映射到白名单策略 sma_cross。", 0.75,
                new Dictionary<string, double> { ["fast"] = 10, ["slow"] = 30 });

        return ("sma_cross", "未识别明确策略类型，默认使用稳健的均线交叉（sma_cross），可在回测后改参。", 0.45,
            new Dictionary<string, double> { ["fast"] = 10, ["slow"] = 30 });
    }

    private static void ApplyStyleHints(string lower, string key, Dictionary<string, double> p)
    {
        var aggressive = ContainsAny(lower, "激进", "短线", "高频", "scalp", "aggressive");
        var conservative = ContainsAny(lower, "保守", "稳健", "长线", "趋势", "conservative");

        if (key == "sma_cross")
        {
            if (aggressive) { p["fast"] = 5; p["slow"] = 20; }
            else if (conservative) { p["fast"] = 20; p["slow"] = 60; }
        }
        else if (key == "rsi")
        {
            if (aggressive) { p["oversold"] = 35; p["overbought"] = 65; }
            else if (conservative) { p["oversold"] = 25; p["overbought"] = 75; }
        }
        else if (key == "donchian")
        {
            if (aggressive) p["period"] = 10;
            else if (conservative) p["period"] = 55;
        }
    }

    private static string InferDuration(string lower)
    {
        if (ContainsAny(lower, "五年", "5年", "5 y", "5y")) return "5 Y";
        if (ContainsAny(lower, "三年", "3年", "3 y", "3y")) return "3 Y";
        if (ContainsAny(lower, "两年", "2年", "2 y", "2y")) return "2 Y";
        if (ContainsAny(lower, "半年", "6个月", "6 m", "6m")) return "6 M";
        if (ContainsAny(lower, "三个月", "3个月", "3 m", "3m", "短线")) return "3 M";
        if (ContainsAny(lower, "一个月", "1个月", "1 m", "1m")) return "1 M";
        return "1 Y";
    }

    private static string InferBarSize(string lower)
    {
        if (ContainsAny(lower, "分钟", "1 min", "5 min", "日内")) return "1 hour";
        if (ContainsAny(lower, "小时", "1 hour")) return "1 hour";
        return "1 day";
    }

    private static bool ContainsAny(string hay, params string[] needles)
        => needles.Any(n => hay.Contains(n, StringComparison.OrdinalIgnoreCase));
}
