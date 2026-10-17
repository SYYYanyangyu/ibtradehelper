using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IBTrade.Application.Ai;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>用现有 LLM 厂商分析期权上下文；无 Key 或失败时走本地启发式，不编造行情。</summary>
public class OptionsAiAdvisor(
    ILlmProviderCatalog catalog,
    IHttpClientFactory httpFactory,
    ILogger<OptionsAiAdvisor>? logger = null) : IOptionsAiAdvisor
{
    public async Task<OptionsAnalyzeResult> AnalyzeAsync(
        OptionsContextDto context, OptionsAnalyzeRequest request, CancellationToken ct = default)
    {
        var (providerId, provider, apiKey) = catalog.ResolveActive();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var llm = provider.ApiStyle.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
                    ? await CallAnthropic(provider, apiKey, context, request, ct)
                    : await CallOpenAi(providerId, provider, apiKey, context, request, ct);
                if (llm is not null)
                    return llm with { Context = context, Source = $"llm:{providerId}" };
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "期权 AI 分析失败，回退启发式");
            }
        }

        var h = Heuristic(context, request);
        if (string.IsNullOrWhiteSpace(apiKey))
            h = h with { Summary = "未配置 LLM Key，以下为基于官方数据的本地启发式（非模型生成）。" + h.Summary };
        else
            h = h with { Summary = "LLM 暂不可用，已回退本地启发式。" + h.Summary };
        return h;
    }

    private async Task<OptionsAnalyzeResult?> CallOpenAi(
        string providerId, LlmProviderOptions provider, string apiKey,
        OptionsContextDto ctx, OptionsAnalyzeRequest req, CancellationToken ct)
    {
        var (system, user) = BuildPrompts(ctx, req);
        var model = string.IsNullOrWhiteSpace(provider.Model) ? (provider.Models.FirstOrDefault() ?? "gpt-5.6-terra") : provider.Model;
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
        if (providerId is "openai" or "deepseek")
            body["response_format"] = new { type = "json_object" };
        if (providerId is "qwen") body["enable_thinking"] = false;
        if (providerId is "deepseek") body["thinking"] = new { type = "disabled" };

        var client = httpFactory.CreateClient("ibtrade-ai");
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/chat/completions");
        httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpReq.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(httpReq, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger?.LogWarning("{Provider} HTTP {Code}: {Body}", providerId, (int)resp.StatusCode, IntelHttp.Clip(raw));
            return null;
        }
        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return Parse(content, ctx);
    }

    private async Task<OptionsAnalyzeResult?> CallAnthropic(
        LlmProviderOptions provider, string apiKey, OptionsContextDto ctx, OptionsAnalyzeRequest req, CancellationToken ct)
    {
        var (system, user) = BuildPrompts(ctx, req);
        var model = string.IsNullOrWhiteSpace(provider.Model) ? "claude-sonnet-5" : provider.Model;
        var body = new
        {
            model,
            max_tokens = 800,
            system,
            messages = new object[] { new { role = "user", content = user } }
        };
        var client = httpFactory.CreateClient("ibtrade-ai");
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/v1/messages");
        httpReq.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        httpReq.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        httpReq.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(httpReq, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger?.LogWarning("Claude HTTP {Code}: {Body}", (int)resp.StatusCode, IntelHttp.Clip(raw));
            return null;
        }
        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        return Parse(content, ctx);
    }

    private static (string System, string User) BuildPrompts(OptionsContextDto ctx, OptionsAnalyzeRequest req)
    {
        var system =
            "你是期权研究助手。只根据给定的官方/公开数据做解读，禁止编造价格、财报数字、概率或未提供的新闻。" +
            "若某数据源失败，明确说缺失。回答必须是 JSON：" +
            "{\"bias\":\"bullish|bearish|neutral\",\"confidence\":0.0,\"summary\":\"...\",\"drivers\":[\"...\"],\"risks\":[\"...\"],\"suggestedFocus\":\"...\"}。" +
            "summary/drivers/risks 用中文。这不是投资建议。";

        var sb = new StringBuilder();
        sb.AppendLine($"标的 {ctx.Symbol} {ctx.CompanyName}");
        if (!string.IsNullOrWhiteSpace(req.Prompt)) sb.AppendLine("用户问题：" + req.Prompt.Trim());
        if (ctx.Underlying is { } u)
            sb.AppendLine($"现价 {u.Last} 涨跌 {u.Change} ({u.ChangePct}%) 状态 {u.MarketStatus} 来源 {u.LastTrade}");
        if (ctx.Stats is { } s)
            sb.AppendLine($"到期 {ctx.SelectedExpiry} ATM {s.AtmStrike} PCR量 {s.PutCallVolumeRatio} PCR仓 {s.PutCallOiRatio} callOI {s.CallOpenInterest} putOI {s.PutOpenInterest}");
        sb.AppendLine("近 ATM 期权（最多 8 行）：");
        foreach (var row in ctx.Chain.Take(8))
            sb.AppendLine($"  {row.Expiry} K{row.Strike} C bid/ask {row.Call.Bid}/{row.Call.Ask} vol {row.Call.Volume} oi {row.Call.OpenInterest} | P {row.Put.Bid}/{row.Put.Ask} vol {row.Put.Volume} oi {row.Put.OpenInterest}");
        sb.AppendLine("财报 surprise：");
        foreach (var e in ctx.Earnings.Take(4))
            sb.AppendLine($"  {e.FiscalQuarter} {e.DateReported} EPS {e.Eps} 预期 {e.Consensus} surprise {e.SurprisePct}%");
        sb.AppendLine("新闻标题：");
        foreach (var n in ctx.News.Take(8))
            sb.AppendLine($"  [{n.Source}] {n.Title}");
        sb.AppendLine("美联储：");
        foreach (var f in ctx.Fed.Take(8))
            sb.AppendLine($"  [{f.Kind}] {f.Date} {f.Title} upcoming={f.Upcoming}");
        sb.AppendLine("美债平均利率：");
        foreach (var t in ctx.Treasury)
            sb.AppendLine($"  {t.Security} {t.RatePct}% asof {t.AsOf}");
        sb.AppendLine("预测市场：");
        if (ctx.PredictionMarkets.Count == 0) sb.AppendLine("  （源失败或无数据）");
        foreach (var p in ctx.PredictionMarkets.Take(6))
            sb.AppendLine($"  {p.Venue} {p.Question} p={p.Probability}");
        sb.AppendLine("数据源状态：" + string.Join("; ", ctx.Sources.Select(x => $"{x.Id}={x.Status}")));
        return (system, sb.ToString());
    }

    private static OptionsAnalyzeResult? Parse(string? content, OptionsContextDto ctx)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var json = content.Trim();
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        using var doc = JsonDocument.Parse(json[start..(end + 1)]);
        var r = doc.RootElement;
        var bias = r.TryGetProperty("bias", out var b) ? b.GetString() ?? "neutral" : "neutral";
        if (bias is not ("bullish" or "bearish" or "neutral")) bias = "neutral";
        var conf = r.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var cf) ? Math.Clamp(cf, 0.1, 0.95) : 0.55;
        var summary = r.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(summary)) return null;
        return new OptionsAnalyzeResult
        {
            Symbol = ctx.Symbol,
            Source = "llm",
            Confidence = conf,
            Bias = bias,
            Summary = summary.Trim(),
            Drivers = StrList(r, "drivers"),
            Risks = StrList(r, "risks"),
            SuggestedFocus = r.TryGetProperty("suggestedFocus", out var f) ? f.GetString() : null,
            Context = ctx
        };
    }

    private static IReadOnlyList<string> StrList(JsonElement r, string name)
    {
        if (!r.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(6).ToList();
    }

    private static OptionsAnalyzeResult Heuristic(OptionsContextDto ctx, OptionsAnalyzeRequest req)
    {
        var drivers = new List<string>();
        var risks = new List<string>();
        var score = 0;
        if (ctx.Stats?.PutCallOiRatio is { } pcr)
        {
            if (pcr > 1.2m) { score -= 1; drivers.Add($"Put/Call 持仓比 {pcr:0.00}，偏防御/对冲。"); }
            else if (pcr < 0.7m) { score += 1; drivers.Add($"Put/Call 持仓比 {pcr:0.00}，Call 持仓更重。"); }
            else drivers.Add($"Put/Call 持仓比 {pcr:0.00}，中性。");
        }
        if (ctx.Underlying?.ChangePct is { } pct)
        {
            if (pct > 0) score += 1;
            if (pct < 0) score -= 1;
            drivers.Add($"现货最近报价变动 {pct:+0.00;-0.00}%（Nasdaq）。");
        }
        var lastEarn = ctx.Earnings.FirstOrDefault();
        if (lastEarn?.SurprisePct is { } sp)
        {
            if (sp > 0) { score += 1; drivers.Add($"最近财报 surprise +{sp}%（{lastEarn.DateReported}）。"); }
            else { score -= 1; drivers.Add($"最近财报 surprise {sp}%（{lastEarn.DateReported}）。"); }
        }
        var nextFomc = ctx.Fed.FirstOrDefault(f => f.Kind == "fomc" && f.Upcoming);
        if (nextFomc is not null)
        {
            risks.Add($"下一场 FOMC：{nextFomc.Title}。利率决议常放大隐含波动。");
            drivers.Add($"美联储官方日历仍有未召开会议：{nextFomc.Date}。");
        }
        if (ctx.Treasury.Count > 0)
            drivers.Add("财政部平均利率：" + string.Join("，", ctx.Treasury.Select(t => $"{t.Security} {t.RatePct}%")));
        if (ctx.PredictionMarkets.Count == 0)
            risks.Add("Polymarket 未取到盘口（常见于出口 IP 被拦），预测市场未纳入。");
        else
            drivers.Add($"预测市场 {ctx.PredictionMarkets.Count} 条（Polymarket）。");
        if (ctx.News.Count > 0)
            drivers.Add($"最新标题：{ctx.News[0].Title}");
        foreach (var src in ctx.Sources.Where(s => s.Status != "ok"))
            risks.Add($"{src.Label} 失败：{src.Message}");
        if (!string.IsNullOrWhiteSpace(req.Prompt))
            drivers.Add("已记录你的问题，但启发式不会扩展未知事实。");

        var bias = score >= 2 ? "bullish" : score <= -2 ? "bearish" : "neutral";
        var focus = ctx.SelectedExpiry is null ? "先选到期日看 ATM 价差" : $"关注 {ctx.SelectedExpiry} ATM {ctx.Stats?.AtmStrike} 跨式宽度";
        return new OptionsAnalyzeResult
        {
            Symbol = ctx.Symbol,
            Source = "heuristic",
            Confidence = 0.42,
            Bias = bias,
            Summary = $"{ctx.Symbol} 当前启发式倾向 {bias}。现价 {ctx.Underlying?.Last}，到期 {ctx.SelectedExpiry}。这不是买卖建议。",
            Drivers = drivers,
            Risks = risks.Count == 0 ? ["公开延迟报价，盘中请以券商实时盘口为准。"] : risks,
            SuggestedFocus = focus,
            Context = ctx
        };
    }
}
