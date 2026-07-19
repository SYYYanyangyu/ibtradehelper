using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IBTrade.Application.Ai;
using IBTrade.Application.Ai.Dtos;
using IBTrade.Application.Backtest.Strategies;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.Ai;

/// <summary>
/// 多厂商 LLM 顾问：openai / deepseek / qwen（OpenAI 兼容）+ claude（Anthropic Messages）。
/// 无 Key 或调用失败 → 启发式回退。
/// </summary>
public class MultiProviderLlmAdvisor(
    HeuristicAiStrategyAdvisor heuristic,
    IStrategyFactory factory,
    ILlmProviderCatalog catalog,
    IHttpClientFactory httpClientFactory,
    ILogger<MultiProviderLlmAdvisor>? logger = null) : IAiStrategyAdvisor
{
    public async Task<AiSuggestResult> SuggestAsync(AiSuggestRequest request, CancellationToken ct = default)
    {
        var (providerId, provider, apiKey) = catalog.ResolveActive();
        if (string.IsNullOrWhiteSpace(apiKey))
            return await heuristic.SuggestAsync(request, ct);

        try
        {
            var llm = provider.ApiStyle.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
                ? await CallAnthropicAsync(provider, apiKey, request, ct)
                : await CallOpenAiCompatibleAsync(providerId, provider, apiKey, request, ct);

            if (llm is null)
                return await heuristic.SuggestAsync(request, ct);

            var symbol = PromptInstrumentResolver.ResolveSymbol(
                request.Prompt, llm.Symbol, request.Symbol);

            var (strategy, parameters) = StrategyParameterSanitizer.SanitizeStrategy(
                factory, llm.StrategyKey, llm.Parameters);

            // 未显式传入时用 LLM 推断；否则尊重请求（但 AI 页默认不再传表单残留）
            var duration = StrategyParameterSanitizer.SanitizeDuration(
                string.IsNullOrWhiteSpace(request.Duration) ? llm.Duration : request.Duration);
            var barSize = StrategyParameterSanitizer.SanitizeBarSize(
                string.IsNullOrWhiteSpace(request.BarSize) ? llm.BarSize : request.BarSize);

            return new AiSuggestResult
            {
                Symbol = symbol,
                StrategyKey = strategy.Key,
                StrategyName = strategy.Name,
                Parameters = parameters,
                Duration = duration,
                BarSize = barSize,
                Source = $"llm:{providerId}",
                Rationale = string.IsNullOrWhiteSpace(llm.Rationale)
                    ? $"[{provider.DisplayName}/{provider.Model}] 建议使用 {strategy.Name}（已校验白名单）。"
                    : $"[{provider.DisplayName}] {llm.Rationale.Trim()}",
                Confidence = Math.Clamp(llm.Confidence <= 0 ? 0.65 : llm.Confidence, 0.1, 0.95)
            };
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "LLM({Provider}) 失败，回退启发式", providerId);
            var fallback = await heuristic.SuggestAsync(request, ct);
            fallback.Rationale = $"LLM({providerId}) 暂不可用，已回退本地启发式。" + fallback.Rationale;
            return fallback;
        }
    }

    private async Task<LlmDraft?> CallOpenAiCompatibleAsync(
        string providerId, LlmProviderOptions provider, string apiKey, AiSuggestRequest request, CancellationToken ct)
    {
        var (system, user) = BuildPrompts(request);
        var model = string.IsNullOrWhiteSpace(provider.Model)
            ? (provider.Models.FirstOrDefault() ?? "gpt-5.6-terra")
            : provider.Model;

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        // 部分国产兼容网关不支持 response_format；OpenAI/DeepSeek 支持
        if (providerId is "openai" or "deepseek")
            body["response_format"] = new { type = "json_object" };
        // 策略路由只需短 JSON：关掉 thinking，避免慢/content 为空
        if (providerId is "qwen")
            body["enable_thinking"] = false;
        if (providerId is "deepseek")
            body["thinking"] = new { type = "disabled" };

        var client = httpClientFactory.CreateClient("ibtrade-ai");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger?.LogWarning("{Provider} HTTP {Code}: {Body}", providerId, (int)resp.StatusCode, Clip(raw));
            return null;
        }

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return ParseDraft(content);
    }

    private async Task<LlmDraft?> CallAnthropicAsync(
        LlmProviderOptions provider, string apiKey, AiSuggestRequest request, CancellationToken ct)
    {
        var (system, user) = BuildPrompts(request);
        var model = string.IsNullOrWhiteSpace(provider.Model)
            ? (provider.Models.FirstOrDefault() ?? "claude-sonnet-5")
            : provider.Model;

        var body = new
        {
            model,
            max_tokens = 1024,
            temperature = 0.2,
            system,
            messages = new object[] { new { role = "user", content = user } }
        };

        var client = httpClientFactory.CreateClient("ibtrade-ai");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/v1/messages");
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger?.LogWarning("Claude HTTP {Code}: {Body}", (int)resp.StatusCode, Clip(raw));
            return null;
        }

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        return ParseDraft(content);
    }

    private (string System, string User) BuildPrompts(AiSuggestRequest request)
    {
        var catalogText = string.Join("\n", factory.List().Select(s =>
        {
            var ps = string.Join(", ", s.Parameters.Select(p =>
                $"{p.Key}(default={p.Default}, min={p.Min}, max={p.Max})"));
            return $"- {s.Key}: {s.Name}; params: {ps}";
        }));

        var system = """
            You are a trading strategy router for IBTrade.
            You MUST ONLY pick a strategy key from the whitelist and numeric parameters within given min/max.
            Never invent new strategies or output code.
            Respond with a single JSON object only, no markdown:
            {"symbol":"SNDK","strategyKey":"rsi","parameters":{"period":14,"oversold":30,"overbought":70},"duration":"1 Y","barSize":"1 day","rationale":"...","confidence":0.7}
            duration one of: 1 D, 60 D, 1 M, 3 M, 6 M, 1 Y, 2 Y, 3 Y, 5 Y, YTD
            barSize one of: 1 min, 5 mins, 15 mins, 30 mins, 1 hour, 1 day
            Symbol rules: if the user mentions a ticker or Chinese company name, use THAT instrument (map 闪迪→SNDK, 苹果→AAPL, 英伟达→NVDA, 特斯拉→TSLA). Never keep a stale default like AAPL when the user named another name/ticker. Correct obvious typos (SANK→SNDK for 闪迪/SanDisk).
            Important: write the "rationale" field in Simplified Chinese (简体中文), 1–3 short sentences explaining why this whitelist strategy and params fit the user request. Do not write English rationale.
            """;

        var user = $"""
            Whitelist strategies:
            {catalogText}

            User request:
            {request.Prompt}

            Preferred symbol (optional, ONLY if user did not name an instrument): {request.Symbol ?? "(infer from request)"}
            Preferred duration (optional): {request.Duration ?? "(infer)"}
            Preferred barSize (optional): {request.BarSize ?? "(infer)"}
            """;
        return (system, user);
    }

    private static LlmDraft? ParseDraft(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var json = content.Trim();
        // 去掉偶发 markdown 围栏
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start) json = json[start..(end + 1)];
        }
        return JsonSerializer.Deserialize<LlmDraft>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static string Clip(string s) => s.Length > 300 ? s[..300] : s;

    private sealed class LlmDraft
    {
        public string? Symbol { get; set; }
        public string? StrategyKey { get; set; }
        public Dictionary<string, double>? Parameters { get; set; }
        public string? Duration { get; set; }
        public string? BarSize { get; set; }
        public string? Rationale { get; set; }
        public double Confidence { get; set; }
    }
}
