using System.Text.RegularExpressions;

namespace IBTrade.Application.Ai;

/// <summary>
/// 从自然语言里解析标的：中文别名优先，再抓 ticker；避免表单默认 AAPL 盖住「闪迪」等意图。
/// </summary>
public static class PromptInstrumentResolver
{
    private static readonly Dictionary<string, string> ChineseAliases = new(StringComparer.Ordinal)
    {
        ["闪迪"] = "SNDK",
        ["苹果"] = "AAPL",
        ["英伟达"] = "NVDA",
        ["英偉達"] = "NVDA",
        ["特斯拉"] = "TSLA",
        ["微软"] = "MSFT",
        ["微軟"] = "MSFT",
        ["亚马逊"] = "AMZN",
        ["亞馬遜"] = "AMZN",
        ["谷歌"] = "GOOGL",
        ["Alphabet"] = "GOOGL",
        ["台积电"] = "TSM",
        ["台積電"] = "TSM",
        ["元宇宙"] = "META",
        ["脸书"] = "META",
    };

    private static readonly HashSet<string> NoiseTickers = new(StringComparer.OrdinalIgnoreCase)
    {
        "RSI", "SMA", "EMA", "MACD", "ETF", "AI", "USD", "YTD", "NAV", "PNL", "FIFO",
        "BUY", "SELL", "LONG", "SHORT", "CALL", "PUT", "ATM", "ITM", "OTM"
    };

    /// <summary>从 prompt 推断标的；找不到返回 null。</summary>
    public static string? TryInferSymbol(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        foreach (var (cn, ticker) in ChineseAliases)
        {
            if (prompt.Contains(cn, StringComparison.OrdinalIgnoreCase))
            {
                try { return StrategyParameterSanitizer.SanitizeSymbol(ticker); }
                catch { /* ignore */ }
            }
        }

        foreach (var token in Regex.Split(prompt, @"[\s,，、/|：:\n\t$]+"))
        {
            var t = token.Trim().TrimStart('$').ToUpperInvariant();
            if (t.Length is < 1 or > 5 || !t.All(char.IsLetter)) continue;
            if (NoiseTickers.Contains(t)) continue;
            try { return StrategyParameterSanitizer.SanitizeSymbol(t); }
            catch { /* ignore */ }
        }

        return null;
    }

    /// <summary>
    /// 解析最终标的：prompt 内提及 &gt; LLM 返回 &gt; 请求显式 symbol &gt; 默认 AAPL。
    /// </summary>
    public static string ResolveSymbol(string? prompt, string? llmSymbol, string? requestSymbol)
    {
        var fromPrompt = TryInferSymbol(prompt);
        if (!string.IsNullOrWhiteSpace(fromPrompt))
            return fromPrompt!;

        if (!string.IsNullOrWhiteSpace(llmSymbol))
        {
            try { return StrategyParameterSanitizer.SanitizeSymbol(llmSymbol); }
            catch { /* fall through */ }
        }

        if (!string.IsNullOrWhiteSpace(requestSymbol))
        {
            try { return StrategyParameterSanitizer.SanitizeSymbol(requestSymbol); }
            catch { /* fall through */ }
        }

        return "AAPL";
    }
}
