namespace IBTrade.Application.Ai;

public record LlmProviderStatusDto(
    string Id,
    string DisplayName,
    string ApiStyle,
    string BaseUrl,
    string Model,
    IReadOnlyList<string> Models,
    bool HasApiKey,
    string ApiKeyEnv);

public record LlmCatalogDto(
    string ActiveProvider,
    IReadOnlyList<LlmProviderStatusDto> Providers);

public interface ILlmProviderCatalog
{
    LlmCatalogDto GetCatalog();

    /// <summary>尝试从厂商拉取模型列表；失败则保留本地默认并返回错误信息。</summary>
    Task<(LlmProviderStatusDto Provider, string? Warning)> RefreshModelsAsync(string providerId, CancellationToken ct = default);

    void SetActiveProvider(string providerId);

    void SetModel(string providerId, string model);

    /// <summary>解析当前生效厂商（含进程内覆盖与环境变量 Key）。</summary>
    (string Id, LlmProviderOptions Provider, string ApiKey) ResolveActive();
}
