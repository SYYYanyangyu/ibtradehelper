using System.Net.Http.Headers;
using System.Text.Json;
using IBTrade.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBTrade.Infrastructure.Ai;

/// <summary>
/// 多厂商目录：状态查询、模型拉取、切换 ActiveProvider/Model（进程内生效）。
/// Key 仍以配置/环境变量为准，不落盘写入密钥。
/// </summary>
public class LlmProviderCatalog(
    IOptionsMonitor<LlmOptions> optionsMonitor,
    IHttpClientFactory httpClientFactory,
    ILogger<LlmProviderCatalog>? logger = null) : ILlmProviderCatalog
{
    private readonly object _gate = new();
    private string? _activeOverride;
    private readonly Dictionary<string, string> _modelOverride = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _modelsCache = new(StringComparer.OrdinalIgnoreCase);

    public LlmCatalogDto GetCatalog()
    {
        var opts = optionsMonitor.CurrentValue;
        var active = _activeOverride ?? opts.ActiveProvider;
        var list = opts.Providers.Select(kv => ToStatus(kv.Key, kv.Value, active)).ToList();
        return new LlmCatalogDto(active, list);
    }

    public void SetActiveProvider(string providerId)
    {
        var opts = optionsMonitor.CurrentValue;
        if (!opts.Providers.ContainsKey(providerId))
            throw new ArgumentException($"未知厂商 '{providerId}'。可用：{string.Join(", ", opts.Providers.Keys)}");
        lock (_gate) _activeOverride = providerId;
    }

    public void SetModel(string providerId, string model)
    {
        var opts = optionsMonitor.CurrentValue;
        if (!opts.Providers.TryGetValue(providerId, out var p))
            throw new ArgumentException($"未知厂商 '{providerId}'");
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("model 不能为空");
        lock (_gate)
        {
            _modelOverride[providerId] = model.Trim();
            if (!_modelsCache.TryGetValue(providerId, out var list))
            {
                list = p.Models.ToList();
                _modelsCache[providerId] = list;
            }
            if (!list.Contains(model, StringComparer.OrdinalIgnoreCase))
                list.Insert(0, model.Trim());
        }
    }

    public async Task<(LlmProviderStatusDto Provider, string? Warning)> RefreshModelsAsync(string providerId, CancellationToken ct = default)
    {
        var opts = optionsMonitor.CurrentValue;
        if (!opts.Providers.TryGetValue(providerId, out var p))
            throw new ArgumentException($"未知厂商 '{providerId}'");

        var key = ResolveApiKey(p);
        if (string.IsNullOrWhiteSpace(key))
            return (ToStatus(providerId, p, _activeOverride ?? opts.ActiveProvider), "未配置 ApiKey，保留本地默认模型列表。");

        try
        {
            var fetched = p.ApiStyle.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
                ? await FetchAnthropicModelsAsync(p, key, ct)
                : await FetchOpenAiCompatibleModelsAsync(p, key, ct);

            if (fetched.Count == 0)
                return (ToStatus(providerId, p, _activeOverride ?? opts.ActiveProvider), "厂商返回空列表，保留本地默认模型。");

            lock (_gate) _modelsCache[providerId] = fetched;
            logger?.LogInformation("已刷新 {Provider} 模型列表：{Count} 个", providerId, fetched.Count);
            return (ToStatus(providerId, p, _activeOverride ?? opts.ActiveProvider), null);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "拉取 {Provider} 模型失败", providerId);
            return (ToStatus(providerId, p, _activeOverride ?? opts.ActiveProvider),
                $"拉取失败，使用本地默认：{ex.Message}");
        }
    }

    /// <summary>供顾问解析当前有效厂商配置（含进程内覆盖）。</summary>
    public (string Id, LlmProviderOptions Provider, string ApiKey) ResolveActive()
    {
        var opts = optionsMonitor.CurrentValue;
        var id = _activeOverride ?? opts.ActiveProvider;
        if (!opts.Providers.TryGetValue(id, out var p))
        {
            // 回退到第一个有 Key 的，再回退 openai
            var withKey = opts.Providers.FirstOrDefault(kv => !string.IsNullOrWhiteSpace(ResolveApiKey(kv.Value)));
            if (!string.IsNullOrEmpty(withKey.Key))
            {
                id = withKey.Key;
                p = withKey.Value;
            }
            else if (!opts.Providers.TryGetValue("openai", out p!))
            {
                p = opts.Providers.Values.First();
                id = opts.Providers.Keys.First();
            }
        }

        lock (_gate)
        {
            if (_modelOverride.TryGetValue(id, out var m) && !string.IsNullOrWhiteSpace(m))
                p = CloneWithModel(p, m);
            else if (_modelsCache.TryGetValue(id, out var cached) && cached.Count > 0
                     && (string.IsNullOrWhiteSpace(p.Model) || !cached.Contains(p.Model, StringComparer.OrdinalIgnoreCase)))
                p = CloneWithModel(p, cached[0]);
        }

        return (id, p, ResolveApiKey(p) ?? "");
    }

    private LlmProviderStatusDto ToStatus(string id, LlmProviderOptions p, string active)
    {
        List<string> models;
        string model;
        lock (_gate)
        {
            models = _modelsCache.TryGetValue(id, out var c) && c.Count > 0
                ? c.ToList()
                : p.Models.ToList();
            model = _modelOverride.TryGetValue(id, out var mo) && !string.IsNullOrWhiteSpace(mo)
                ? mo
                : (string.IsNullOrWhiteSpace(p.Model) && models.Count > 0 ? models[0] : p.Model);
        }

        return new LlmProviderStatusDto(
            id,
            string.IsNullOrWhiteSpace(p.DisplayName) ? id : p.DisplayName,
            p.ApiStyle,
            p.BaseUrl,
            model,
            models,
            !string.IsNullOrWhiteSpace(ResolveApiKey(p)),
            p.ApiKeyEnv ?? "");
    }

    private static string? ResolveApiKey(LlmProviderOptions p)
    {
        if (!string.IsNullOrWhiteSpace(p.ApiKeyEnv))
        {
            var fromEnv = Environment.GetEnvironmentVariable(p.ApiKeyEnv);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
        }
        return string.IsNullOrWhiteSpace(p.ApiKey) ? null : p.ApiKey.Trim();
    }

    private static LlmProviderOptions CloneWithModel(LlmProviderOptions p, string model) => new()
    {
        DisplayName = p.DisplayName,
        ApiKey = p.ApiKey,
        BaseUrl = p.BaseUrl,
        ApiStyle = p.ApiStyle,
        Model = model,
        Models = p.Models.ToList(),
        ApiKeyEnv = p.ApiKeyEnv
    };

    private async Task<List<string>> FetchOpenAiCompatibleModelsAsync(LlmProviderOptions p, string apiKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ibtrade-ai");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{p.BaseUrl.TrimEnd('/')}/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Trim(raw)}");

        using var doc = JsonDocument.Parse(raw);
        var ids = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var s = id.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
                }
            }
        }
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Task<List<string>> FetchAnthropicModelsAsync(LlmProviderOptions p, string apiKey, CancellationToken ct)
    {
        // Anthropic 公开 models 列表因账号/版本差异大；优先尝试 GET /v1/models，失败由调用方保留默认。
        return FetchOpenAiStyleAnthropicAsync(p, apiKey, ct);
    }

    private async Task<List<string>> FetchOpenAiStyleAnthropicAsync(LlmProviderOptions p, string apiKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("ibtrade-ai");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{p.BaseUrl.TrimEnd('/')}/v1/models");
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Trim(raw)}");

        using var doc = JsonDocument.Parse(raw);
        var ids = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var s = id.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
                }
            }
        }
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
}
